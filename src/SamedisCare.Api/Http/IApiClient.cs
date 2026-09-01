namespace SamedisCare.Api.Http;

/// <summary>
/// The requests the resource classes make. <see cref="RequestData"/> is the real
/// implementation; the interface exists so lookup and upsert logic can be tested without
/// a server.
/// <para>
/// Status is read from <see cref="StatusCode"/> after a call rather than returned,
/// because that is how <see cref="RequestData"/> has always worked and the resource
/// classes rely on it.
/// </para>
/// </summary>
public interface IApiClient
{
    /// <summary>HTTP status of the last request.</summary>
    int StatusCode { get; }

    /// <summary>Body of the last response, for reading <c>meta.msg</c> after a failure.</summary>
    string LastContent { get; }

    /// <summary>
    /// Transport-level error of the last request -- a name that would not resolve, a refused
    /// connection, a timeout. Empty when the request reached the server, whatever it then
    /// answered: a 403 leaves this empty and says its piece in <see cref="LastContent"/>.
    /// </summary>
    string LastError { get; }

    /// <summary>
    /// Whether this is a dry run. The client does not suppress writes on its own -- callers
    /// gate them, so that a tool decides what it does not do rather than the transport
    /// silently not doing what it was asked.
    /// </summary>
    bool TestMode { get; }

    string Get(string resource);

    string Post(string resource, string content);

    /// <summary>PUT to <c>{resource}/{id}</c>.</summary>
    string Put(string resource, string id, string content);

    string PostDocument(string resource, string filePath, string fileName);
}
