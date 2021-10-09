// See https://aka.ms/new-console-template for more information
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using RoslynFastStringSwitchPoc;

//const string code = @"public class MyClass
//{
//    public bool IsKnownContentType(string contentTypeValue)
//    {
//        switch (contentTypeValue)
//        {
//            case ""text/xml"":
//            case ""text/css"":
//            case ""text/csv"":
//            case ""image/gif"":
//            case ""image/png"":
//            case ""text/html"":
//            case ""text/plain"":
//            case ""image/jpeg"":
//            case ""application/pdf"":
//            case ""application/xml"":
//            case ""application/zip"":
//            case ""application/grpc"":
//            case ""application/json"":
//            case ""multipart/form-data"":
//            case ""application/javascript"":
//            case ""application/octet-stream"":
//            case ""text/html; charset=utf-8"":
//            case ""text/plain; charset=utf-8"":
//            case ""application/json; charset=utf-8"":
//            case ""application/x-www-form-urlencoded"":
//                return true;
//        }
//        return false;
//    }
//}";

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
