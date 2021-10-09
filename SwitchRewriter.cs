using System.Collections.Immutable;
using System.Diagnostics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace RoslynFastStringSwitchPoc
{
    internal class SwitchRewriter : CSharpSyntaxRewriter
    {
        private readonly Compilation _compilation;
        private readonly SemanticModel _model;
        private readonly INamedTypeSymbol _stringType;
        private int _tempVarCounter = 0;

        public SwitchRewriter(Compilation compilation, SemanticModel model)
        {
            _compilation = compilation;
            _model = model;
            _stringType = _compilation.GetSpecialType(SpecialType.System_String);
        }

        private string GetTempVarName(string name)
        {
            var num = _tempVarCounter++;
            return $"__{name}__{num}";
        }

        public override SyntaxNode? VisitSwitchStatement(SwitchStatementSyntax node)
        {
            // Bail out if:
            //     - We can't get the IOperation for the switch;
            //     - The switch isn't over a string;
            //     - The switch has any locals (should be easy to add support for);
            //     - Any of the switch arms has pattern matching.
            if (_model.GetOperation(node) is not ISwitchOperation switchOp
                || !SymbolEqualityComparer.Default.Equals(switchOp.Value.Type, _stringType)
                || switchOp.Locals.Any()
                || switchOp.Cases.Any(@case => @case.Clauses.Any(clause => clause.CaseKind is not CaseKind.SingleValue or CaseKind.Default)))
            {
                return node;
            }

            // Split up all switch sections into a list of bodies (with identifiers)
            // and a list of clauses and which body they should go to.
            var cases = switchOp.Cases;
            var bodies = new List<SwitchCaseBody>();
            var clauses = new List<SwitchClause>();
            var defaultClause = -1;
            var nullClause = -1;
            for (var idx = 0; idx < cases.Length; idx++)
            {
                var @case = cases[idx];
                var caseSyntax = (SwitchSectionSyntax) @case.Syntax;
                // Visit the section to process nested switches.
                var visitedCaseSyntax = (SwitchSectionSyntax) Visit(caseSyntax);
                bodies.Add(new SwitchCaseBody(idx, visitedCaseSyntax.Statements));
                foreach (var clause in @case.Clauses)
                {
                    if (clause.CaseKind == CaseKind.Default)
                    {
                        Debug.Assert(defaultClause == -1);
                        defaultClause = idx;
                    }
                    else if (clause.CaseKind == CaseKind.SingleValue)
                    {
                        var singleValClause = (ISingleValueCaseClauseOperation) clause;
                        var value = (string?) singleValClause.Value.ConstantValue.Value;
                        if (value is null)
                        {
                            Debug.Assert(nullClause == -1);
                            nullClause = idx;
                        }
                        else
                        {
                            clauses.Add(new SwitchClause(value, idx));
                        }
                    }
                }
            }

            // Group the clauses by the key length.
            var groupedClauses = clauses.GroupBy(clause => clause.Key.Length);
            // Then find the unique char positions per length candidates.
            ImmutableArray<LengthSwitchSection> perLengthSections = groupedClauses.Select(group =>
                new LengthSwitchSection(
                    group.Key,
                    group.ToImmutableArray(),
                    StringHelper.GetUniqueColumnLocation(group.Select(clause => clause.Key))))
                                                .ToImmutableArray();
            // If we fail to find any unique ranges for all cases, then there's nothing we can do.
            // If we had other optimized implementations for switching then we'd remove this and
            // put the other optimizers into NormalStringSwitch.
            if (perLengthSections.All(arr => arr.CheckIndex == -1))
                return node;

            var expressionVarName = GetTempVarName("switchExpr");
            var candidateVarName = GetTempVarName("candidate");
            var destinationVarName = GetTempVarName("destination");
            var blockStatements = new List<StatementSyntax>
            {
                // var __switchExpr__0 = <expr>;
                LocalDeclarationStatement(
                    VariableDeclaration(
                        IdentifierName("var"),
                        SingletonSeparatedList(
                            VariableDeclarator(
                                Identifier(expressionVarName),
                                null,
                                EqualsValueClause(node.Expression))))),
                // string? __candidate__1 = null;
                LocalDeclarationStatement(
                    VariableDeclaration(
                        NullableType(PredefinedType(Token(SyntaxKind.StringKeyword))),
                        SingletonSeparatedList(
                            VariableDeclarator(
                                Identifier(candidateVarName),
                                null,
                                EqualsValueClause(LiteralExpression(SyntaxKind.NullLiteralExpression)))))),
                // int __destination__2 = -1;
                LocalDeclarationStatement(
                    VariableDeclaration(
                        PredefinedType(Token(SyntaxKind.IntKeyword)),
                        SingletonSeparatedList(
                            VariableDeclarator(
                                Identifier(destinationVarName),
                                null,
                                EqualsValueClause(
                                    NumericLiteral(-1)))))),
            };
            if (nullClause != -1)
            {
                // if (__switchExpr__0 == null)
                // {
                //     __destination__2 = <null clause idx>;
                // }
                // else
                // {
                //     switch (__switchExpr__0.Length)
                //     {
                //         ...
                //     }
                // }
                blockStatements.Add(IfStatement(
                    BinaryExpression(
                        SyntaxKind.EqualsExpression,
                        IdentifierName(expressionVarName),
                        LiteralExpression(SyntaxKind.NullLiteralExpression)),
                    Block(
                        ExpressionStatement(
                            AssignmentExpression(
                                SyntaxKind.SimpleAssignmentExpression,
                                IdentifierName(destinationVarName),
                                NumericLiteral(nullClause)))),
                    ElseClause(MainSwitch(
                        expressionVarName,
                        candidateVarName,
                        destinationVarName,
                        perLengthSections))));
            }
            else
            {
                blockStatements.Add(MainSwitch(
                    expressionVarName,
                    candidateVarName,
                    destinationVarName,
                    perLengthSections));
            }

            ElseClauseSyntax? elseClause = null;
            if (defaultClause != -1)
                elseClause = ElseClause(Block(bodies[defaultClause].Body));

            blockStatements.Add(IfStatement(
                BinaryExpression(
                    SyntaxKind.LogicalAndExpression,
                    // __destination__2 != -1
                    BinaryExpression(
                        SyntaxKind.NotEqualsExpression,
                        IdentifierName(destinationVarName),
                        NumericLiteral(-1)),
                    // <expr equals candidate>
                    ParenthesizedExpression(
                        CandidateEqualityCheck(
                            expressionVarName,
                            candidateVarName))),
                // {
                //    <destination switch>
                // }
                Block(
                    DestinationsSwitch(
                        destinationVarName,
                        bodies)),
                elseClause));

            return Block(blockStatements);
        }

        /// <summary>
        /// Generates the switch that finds the candidate string and possible destination
        /// for the provided input.
        /// </summary>
        /// <remarks>
        /// This will contain a mix of fast switches (that check unique chars) and normal switches
        /// (that fall back to the stock string switch currently).
        /// </remarks>
        /// <param name="expressionVarName"></param>
        /// <param name="candidateVarName"></param>
        /// <param name="destinationVarName"></param>
        /// <param name="perLengthSections"></param>
        /// <returns></returns>
        private static SwitchStatementSyntax MainSwitch(
            string expressionVarName,
            string candidateVarName,
            string destinationVarName,
            ImmutableArray<LengthSwitchSection> perLengthSections)
        {
            var sections = new List<SwitchSectionSyntax>(perLengthSections.Length + 1);
            foreach (var section in perLengthSections)
            {
                if (section.Clauses.Length == 1)
                {
                    var clause = section.Clauses[0];
                    // case <n>:
                    //     __candidate__1 = "<key>";
                    //     __destination__2 = <idx>;
                    //     break;
                    sections.Add(ConstantSwitchSection(
                        NumericLiteral(section.Length),
                        CandidateAndDestinationAssignment(candidateVarName, destinationVarName, clause)));
                }
                else
                {
                    if (section.CheckIndex != -1)
                    {
                        sections.Add(UniqueCharSwitch(
                            expressionVarName,
                            candidateVarName,
                            destinationVarName,
                            section));
                    }
                    else
                    {
                        // TODO: Turn this into a trie or a check that compares multiple
                        //       characters at the same time.
                        sections.Add(NormalStringSwitch(
                            expressionVarName,
                            candidateVarName,
                            destinationVarName,
                            section));
                    }
                }
            }

            return SwitchStatement(
                MemberAccessExpression(
                    SyntaxKind.SimpleMemberAccessExpression,
                    IdentifierName(expressionVarName),
                    IdentifierName("Length")),
                List(sections));
        }

        /// <summary>
        /// Creates a switch section with an inner switch statement that switches over characters at
        /// an index we have verified is unique for every candidate.
        /// </summary>
        /// <param name="expressionVarName"></param>
        /// <param name="candidateVarName"></param>
        /// <param name="destinationVarName"></param>
        /// <param name="section"></param>
        /// <returns></returns>
        private static SwitchSectionSyntax UniqueCharSwitch(
            string expressionVarName,
            string candidateVarName,
            string destinationVarName,
            LengthSwitchSection section)
        {
            var innerSwitchClauses = new List<SwitchSectionSyntax>(section.Clauses.Length);
            foreach (var clause in section.Clauses)
            {
                innerSwitchClauses.Add(ConstantSwitchSection(
                    CharLiteral(clause.Key[section.CheckIndex]),
                    CandidateAndDestinationAssignment(candidateVarName, destinationVarName, clause)));
            }

            // case <len>:
            return ConstantSwitchSection(
                NumericLiteral(section.Length),
                List(new StatementSyntax[]
                {
                            // switch (<expr>[<idx>])
                            // {
                            //     case <ch>:
                            //         ...
                            // }
                            SwitchStatement(
                                ElementAccessExpression(
                                    IdentifierName(expressionVarName),
                                    BracketedArgumentList(
                                        SingletonSeparatedList(
                                            Argument(NumericLiteral(section.CheckIndex))))),
                                List(innerSwitchClauses)),
                            // break;
                            BreakStatement()
                }));
        }

        /// <summary>
        /// Creates a switch section with an inner switch statement that switches over normal strings.
        /// </summary>
        /// <remarks>
        /// This should be replaced with another implementation that generates a trie or checks a range
        /// of characters instead of a single one (or both as the latter still has cases where it will
        /// not be efficient enough or will work at all).
        /// </remarks>
        /// <param name="expressionVarName"></param>
        /// <param name="candidateVarName"></param>
        /// <param name="destinationVarName"></param>
        /// <param name="section"></param>
        /// <returns></returns>
        private static SwitchSectionSyntax NormalStringSwitch(
            string expressionVarName,
            string candidateVarName,
            string destinationVarName,
            LengthSwitchSection section)
        {
            var innerSwitchClauses = new List<SwitchSectionSyntax>(section.Clauses.Length);
            foreach (var clause in section.Clauses)
            {
                innerSwitchClauses.Add(ConstantSwitchSection(
                    StringLiteral(clause.Key),
                    List(new StatementSyntax[]
                    {
                        // __candidate__1 = __switchExpr__0;
                        ExpressionStatement(AssignmentExpression(
                            SyntaxKind.SimpleAssignmentExpression,
                            IdentifierName(candidateVarName),
                            IdentifierName(expressionVarName))),
                        // __destination__2 = <destination>;
                        ExpressionStatement(AssignmentExpression(
                            SyntaxKind.SimpleAssignmentExpression,
                            IdentifierName(destinationVarName),
                            NumericLiteral(clause.Index))),
                        // break;
                        BreakStatement()
                    })));
            }

            // case <len>:
            return ConstantSwitchSection(
                NumericLiteral(section.Length),
                List(new StatementSyntax[]
                {
                            // switch (<expr>)
                            // {
                            //     case <key[1]>:
                            //         ...
                            // }
                            SwitchStatement(
                                IdentifierName(expressionVarName),
                                List(innerSwitchClauses)),
                            // break;
                            BreakStatement()
                }));
        }

        /// <summary>
        /// Creates a switch with all non-default destination blocks.
        /// </summary>
        /// <remarks>
        /// The default destination block isn't included here as we check for it in
        /// an earlier <c>if</c> condition and put the contents of that destination
        /// in an <c>else</c> clause.
        /// <para>
        /// p.s.: this does mean the default block can get duplicated if a case label
        /// other than the default label also shares the body with default.
        /// </para>
        /// </remarks>
        /// <param name="destinationVarName"></param>
        /// <param name="switchCaseBodies"></param>
        /// <returns></returns>
        private static SwitchStatementSyntax DestinationsSwitch(
            string destinationVarName,
            List<SwitchCaseBody> switchCaseBodies)
        {
            var sections = new List<SwitchSectionSyntax>(switchCaseBodies.Count);
            foreach (var switchCaseBody in switchCaseBodies)
            {
                sections.Add(ConstantSwitchSection(
                    NumericLiteral(switchCaseBody.Index),
                    switchCaseBody.Body));
            }
#if DEBUG
            // default:
            sections.Add(SwitchSection(
                SingletonList<SwitchLabelSyntax>(
                    DefaultSwitchLabel()),
                List(new StatementSyntax[]
                {
                    // System.Diagnostics.Debug.Fail("Default case was hit.");
                    ExpressionStatement(InvocationExpression(
                        MemberAccessExpression(
                            SyntaxKind.SimpleMemberAccessExpression,
                            MemberAccessExpression(
                                SyntaxKind.SimpleMemberAccessExpression,
                                MemberAccessExpression(
                                    SyntaxKind.SimpleMemberAccessExpression,
                                    IdentifierName("System"),
                                    IdentifierName("Diagnostics")),
                                IdentifierName("Debug")),
                            IdentifierName("Fail")),
                        ArgumentList(SingletonSeparatedList(
                            Argument(StringLiteral("Default case was hit.")))))),
                    // break;
                    BreakStatement()
                })));
#endif
            return SwitchStatement(
                IdentifierName(destinationVarName),
                List(sections));
        }

        /// <summary>
        /// Generates an equality check for the expression variable and candidate variable.
        /// </summary>
        /// <param name="expressionVarName"></param>
        /// <param name="candidateVarName"></param>
        /// <returns></returns>
        private static BinaryExpressionSyntax CandidateEqualityCheck(
            string expressionVarName,
            string candidateVarName)
        {
            return BinaryExpression(
                SyntaxKind.LogicalOrExpression,
                // object.ReferenceEquals(__switchExpr__0, __candidate__1)
                InvocationExpression(
                    MemberAccessExpression(
                        SyntaxKind.SimpleMemberAccessExpression,
                        PredefinedType(Token(SyntaxKind.ObjectKeyword)),
                        IdentifierName("ReferenceEquals")),
                    ArgumentList(SeparatedList(new ArgumentSyntax[]
                    {
                        Argument(IdentifierName(expressionVarName)),
                        Argument(IdentifierName(candidateVarName)),
                    }))),
                // || __switchExpr__0?.Equals(__candidate__1) is true
                BinaryExpression(
                    SyntaxKind.IsExpression,
                    ConditionalAccessExpression(
                        IdentifierName(expressionVarName),
                        InvocationExpression(
                            MemberBindingExpression(
                                IdentifierName("Equals")),
                            ArgumentList(SingletonSeparatedList(
                                Argument(IdentifierName(candidateVarName)))))),
                    LiteralExpression(SyntaxKind.TrueLiteralExpression)));
        }

        /// <summary>
        /// Generates a list of statements that:
        /// <list type="number">
        /// <item>assigns the key from <paramref name="clause"/> to the candidate var;</item>
        /// <item>assigns the destination block number to the destination var;</item>
        /// <item>breaks out of the current switch section.</item>
        /// </list>
        /// </summary>
        /// <param name="candidateVarName"></param>
        /// <param name="destinationVarName"></param>
        /// <param name="clause"></param>
        /// <returns></returns>
        private static SyntaxList<StatementSyntax> CandidateAndDestinationAssignment(
            string candidateVarName,
            string destinationVarName,
            SwitchClause clause)
        {
            return List(new StatementSyntax[]
            {
                // __candidate__1 = <key>;
                ExpressionStatement(AssignmentExpression(
                    SyntaxKind.SimpleAssignmentExpression,
                    IdentifierName(candidateVarName),
                    StringLiteral(clause.Key))),
                // __destination__2 = <destination>;
                ExpressionStatement(AssignmentExpression(
                    SyntaxKind.SimpleAssignmentExpression,
                    IdentifierName(destinationVarName),
                    NumericLiteral(clause.Index))),
                // break;
                BreakStatement()
            });
        }

        #region SyntaxNode Creation Helpers

        private static SwitchSectionSyntax ConstantSwitchSection(
            LiteralExpressionSyntax literal,
            SyntaxList<StatementSyntax> statements) =>
            SwitchSection(
                SingletonList<SwitchLabelSyntax>(CaseSwitchLabel(literal)),
                statements);

        private static LiteralExpressionSyntax NumericLiteral(int value) =>
            LiteralExpression(SyntaxKind.NumericLiteralExpression, Literal(value));
        private static LiteralExpressionSyntax StringLiteral(string value) =>
            LiteralExpression(SyntaxKind.StringLiteralExpression, Literal(value));
        private static LiteralExpressionSyntax CharLiteral(char value) =>
            LiteralExpression(SyntaxKind.CharacterLiteralExpression, Literal(value));

        #endregion SyntaxNode Creation Helpers

        private readonly record struct SwitchClause(string Key, int Index);
        private readonly record struct SwitchCaseBody(int Index, SyntaxList<StatementSyntax> Body);
        private readonly record struct LengthSwitchSection(int Length, ImmutableArray<SwitchClause> Clauses, int CheckIndex);
    }
}

