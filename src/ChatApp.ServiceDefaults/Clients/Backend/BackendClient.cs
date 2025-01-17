using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using ChatApp.ServiceDefaults.Contracts;

namespace ChatApp.ServiceDefaults.Clients.Backend;

public class BackendClient(HttpClient http)
{
    public async IAsyncEnumerable<AIChatCompletionDelta> ChatAsync(AIChatRequest request, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var httpRequest = new HttpRequestMessage(HttpMethod.Post, "/chat/stream")
        {
            Content = JsonContent.Create(request),
        };
        var response = await http.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        await foreach (var item in JsonSerializer.DeserializeAsyncEnumerable<AIChatCompletionDelta>(stream, true, cancellationToken: cancellationToken))
        {
            if (item is not null)
            {
                yield return item;
            }
        }
    }
}
