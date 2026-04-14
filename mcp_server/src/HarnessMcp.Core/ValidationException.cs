namespace HarnessMcp.Core;

public sealed class ValidationException : Exception
{
    public ValidationException(string message) : base(message)
    {
    }
}
