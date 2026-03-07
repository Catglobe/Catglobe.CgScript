using System.Runtime.Serialization;
using Microsoft.VisualStudio.LanguageServer.Protocol;
using LspRange = Microsoft.VisualStudio.LanguageServer.Protocol.Range;

namespace Catglobe.CgScript.EditorSupport.Lsp.Handlers;

/// <summary>
/// Params for <c>textDocument/selectionRange</c>.
/// Defined locally because VS LSP 17.2.x does not expose these types.
/// Serialized by Newtonsoft.Json via <see cref="DataMemberAttribute"/> names,
/// matching the VS LSP library's own pattern.
/// </summary>
[DataContract]
public sealed class SelectionRangeParams
{
   [DataMember(Name = "textDocument")]
   public TextDocumentIdentifier TextDocument { get; set; } = null!;

   [DataMember(Name = "positions")]
   public Position[] Positions { get; set; } = [];
}

/// <summary>
/// A single selection-range entry in the <c>textDocument/selectionRange</c> response.
/// <see cref="Parent"/> points to the next larger enclosing range (null at the outermost).
/// </summary>
[DataContract]
public sealed class CgSelectionRange
{
   [DataMember(Name = "range")]
   public LspRange Range { get; set; } = null!;

   /// <summary>
   /// The next larger enclosing range, or <c>null</c> for the outermost node.
   /// </summary>
   [DataMember(Name = "parent", EmitDefaultValue = false)]
   public CgSelectionRange? Parent { get; set; }
}
