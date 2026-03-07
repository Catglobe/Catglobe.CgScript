using Catglobe.CgScript.EditorSupport.Lsp;
using Catglobe.CgScript.EditorSupport.Lsp.Definitions;
using Catglobe.CgScript.EditorSupport.Lsp.Handlers;
using System.IO.Pipelines;

// Redirect stderr so logging doesn't corrupt the JSON-RPC stream on stdout.
Console.OutputEncoding = System.Text.Encoding.UTF8;

var definitions = new DefinitionLoader();
var store       = new DocumentStore(definitions);
var target      = new CgScriptLanguageTarget(store, definitions);

var stdin  = Console.OpenStandardInput();
var stdout = Console.OpenStandardOutput();

var pipe = new DuplexPipe(
   PipeReader.Create(stdin),
   PipeWriter.Create(stdout));

await LspSessionHost.RunAsync(pipe, target);

file sealed class DuplexPipe(PipeReader reader, PipeWriter writer) : IDuplexPipe
{
   public PipeReader Input  => reader;
   public PipeWriter Output => writer;
}
