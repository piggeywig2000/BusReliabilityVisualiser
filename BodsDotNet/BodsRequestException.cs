using System.Net;

namespace BodsDotNet
{
    internal class BodsRequestException(string message, HttpStatusCode statusCode) : HttpRequestException(message, null, statusCode)
    {
    }
}
