using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using RoslynFastStringSwitchPoc;

string uriOrPath;
if (args.Length == 1)
{
    uriOrPath = args[0];
}
else
{
    Console.Write("Path or URL to C# code file: ");
    uriOrPath = Console.ReadLine() ?? throw new Exception("Invalid url or path.");
}

string code;
if (uriOrPath.StartsWith("http://") || uriOrPath.StartsWith("https://"))
{
    using (var client = new HttpClient())
        code = await client.GetStringAsync(uriOrPath);
}
else
{
    code = File.ReadAllText(uriOrPath);
}

var tree = CSharpSyntaxTree.ParseText(code);
var compilation = CSharpCompilation.Create(
    "MyAssembly",
    new[] { tree },
    new[] { MetadataReference.CreateFromFile(typeof(string).Assembly.Location) });
var semantic = compilation.GetSemanticModel(tree);
var root = tree.GetRoot();
var rewriter = new SwitchRewriter(compilation, semantic);
var optimized = rewriter.Visit(root).NormalizeWhitespace();
optimized.WriteTo(Console.Out);
