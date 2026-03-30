using Catglobe.CgScript.EditorSupport.Lsp;
using Catglobe.CgScript.EditorSupport.Lsp.Definitions;
using Catglobe.CgScript.EditorSupport.Lsp.Handlers;
using Microsoft.Extensions.Logging;
using System.IO.Pipelines;

// stdout is the JSON-RPC stream — all logging must go to stderr.
Console.OutputEncoding = System.Text.Encoding.UTF8;

using var loggerFactory = LoggerFactory.Create(b => b
   .AddProvider(new StderrLoggerProvider())
   .SetMinimumLevel(LogLevel.Information));
var logger = loggerFactory.CreateLogger("CgScript.Definitions");

var siteUrl     = args.SkipWhile(a => a != "--site").Skip(1).FirstOrDefault();
var definitions = siteUrl is not null
   ? await DefinitionLoader.CreateFromUrlAsync(siteUrl, logger)
   : new DefinitionLoader();
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

file sealed class StderrLoggerProvider : ILoggerProvider
{
   public ILogger CreateLogger(string categoryName) => new StderrLogger(categoryName);
   public void Dispose() { }
}

file sealed class StderrLogger(string _) : ILogger
{
   public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
   public bool IsEnabled(LogLevel level) => level >= LogLevel.Information;
   public void Log<TState>(LogLevel level, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
   {
      var prefix = level switch
      {
         LogLevel.Warning  => "warn",
         LogLevel.Error    => "error",
         LogLevel.Critical => "crit",
         _                 => "info",
      };
      Console.Error.WriteLine($"[{DateTime.Now:HH:mm:ss} {prefix}] {formatter(state, exception)}");
      if (exception is not null)
         Console.Error.WriteLine(exception.ToString());
   }
}
