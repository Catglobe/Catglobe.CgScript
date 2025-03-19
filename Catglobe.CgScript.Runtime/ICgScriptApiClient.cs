using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace Catglobe.CgScript.Runtime;

/// <summary>
/// Client to execute scripts on the server.
/// </summary>
public interface ICgScriptApiClient
{
   /// <summary>
   /// Execute a script on the server.
   /// </summary>
   /// <param name="scriptName">Name of script to run</param>
   /// <param name="parameter">The parameter for the script</param>
   /// <param name="callJsonTypeInfo">Source generator parser for the parameter</param>
   /// <param name="resultJsonTypeInfo">Source generator parser for the result</param>
   /// <param name="cancellationToken">Token to monitor for cancellation requests</param>
   /// <returns>A results object that contains the message from the server</returns>
   public Task<ScriptResult<TR>> Execute<TP, TR>(string scriptName, TP parameter, JsonTypeInfo<TP> callJsonTypeInfo, JsonTypeInfo<TR> resultJsonTypeInfo, CancellationToken cancellationToken = default);

   /// <summary>
   /// Execute a script on the server with the parameters expanded, i.e. parameter must serialize to an array.
   /// </summary>
   /// <param name="scriptName">Name of script to run</param>
   /// <param name="parameter">The parameter for the script</param>
   /// <param name="callJsonTypeInfo">Source generator parser for the parameter</param>
   /// <param name="resultJsonTypeInfo">Source generator parser for the result</param>
   /// <param name="cancellationToken">Token to monitor for cancellation requests</param>
   /// <returns>A results object that contains the message from the server</returns>
   public Task<ScriptResult<TR>> ExecuteArray<TP, TR>(string scriptName, TP parameter, JsonTypeInfo<TP> callJsonTypeInfo, JsonTypeInfo<TR> resultJsonTypeInfo, CancellationToken cancellationToken = default);

   /// <summary>
   /// Execute a script on the server.
   /// </summary>
   /// <param name="scriptName">Name of script to run</param>
   /// <param name="resultJsonTypeInfo">Source generator parser for the result</param>
   /// <param name="cancellationToken">Token to monitor for cancellation requests</param>
   /// <returns>A results object that contains the message from the server</returns>
   public Task<ScriptResult<TR>> Execute<TR>(string scriptName, JsonTypeInfo<TR> resultJsonTypeInfo, CancellationToken cancellationToken = default);

   /// <summary>
   /// Execute a script on the server.
   /// </summary>
   /// <remarks>
   /// Not recommended to use. Prefer a model that uses <see cref="Execute{TP,TR}(string,TP,JsonTypeInfo{TP},JsonTypeInfo{TR},CancellationToken)"/>.
   /// </remarks>
   /// <param name="scriptName">Name of script to run</param>
   /// <param name="parameter">The parameter for the script</param>
   /// <param name="options">Options that will tell us how to serialize parameters and deserialize result. Can be null</param>
   /// <param name="cancellationToken">Token to monitor for cancellation requests</param>
   /// <returns>A results object that contains the message from the server</returns>
   [RequiresUnreferencedCode("JSON")]
   public Task<ScriptResult<TR>> Execute<TP, TR>(string scriptName, TP parameter, JsonSerializerOptions? options = null, CancellationToken cancellationToken = default);

   /// <summary>
   /// Execute a script on the server with the parameters expanded, i.e. parameter must serialize to an array.
   /// </summary>
   /// <remarks>
   /// Not recommended to use. Prefer a model that uses <see cref="ExecuteArray{TP,TR}(string,TP,JsonTypeInfo{TP},JsonTypeInfo{TR},CancellationToken)"/>.
   /// </remarks>
   /// <param name="scriptName">Name of script to run</param>
   /// <param name="parameter">The parameter for the script</param>
   /// <param name="options">Options that will tell us how to serialize parameters and deserialize result. Can be null</param>
   /// <param name="cancellationToken">Token to monitor for cancellation requests</param>
   /// <returns>A results object that contains the message from the server</returns>
   [RequiresUnreferencedCode("JSON")]
   public Task<ScriptResult<TR>> ExecuteArray<TP, TR>(string scriptName, TP parameter, JsonSerializerOptions? options = null, CancellationToken cancellationToken = default);

   /// <summary>
   /// Execute a script on the server.
   /// </summary>
   /// <remarks>
   /// Not recommended to use. Prefer a model that uses <see cref="Execute{TP,TR}(string,TP,JsonTypeInfo{TP},JsonTypeInfo{TR},CancellationToken)"/>.
   /// </remarks>
   /// <param name="scriptName">Name of script to run</param>
   /// <param name="parameters">The parameters for the script</param>
   /// <param name="options">Options that will tell us how to serialize parameters and deserialize result. Can be null</param>
   /// <param name="cancellationToken">Token to monitor for cancellation requests</param>
   /// <returns>A results object that contains the message from the server</returns>
   [RequiresUnreferencedCode("JSON")]
   public Task<ScriptResult<TR>> Execute<TR>(string scriptName, IReadOnlyCollection<object> parameters, JsonSerializerOptions? options = null, CancellationToken cancellationToken = default);

   /// <summary>
   /// Execute a script on the server.
   /// </summary>
   /// <remarks>
   /// Not recommended to use. Prefer a model that uses <see cref="Execute{TP,TR}(string,TP,JsonTypeInfo{TP},JsonTypeInfo{TR},CancellationToken)"/>.
   /// </remarks>
   /// <param name="scriptName">Name of script to run</param>
   /// <param name="options">Options that will tell us how to serialize parameters and deserialize result. Can be null</param>
   /// <param name="cancellationToken">Token to monitor for cancellation requests</param>
   /// <returns>A results object that contains the message from the server</returns>
   [RequiresUnreferencedCode("JSON")]
   public Task<ScriptResult<TR>> Execute<TR>(string scriptName, JsonSerializerOptions? options = null, CancellationToken cancellationToken = default);

}
