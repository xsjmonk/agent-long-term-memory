using HarnessMcp.Contracts;

namespace HarnessMcp.Core;

public sealed class AuthorityPolicy : IAuthorityPolicy
{
    public bool IsAllowed(AuthorityLevel actual, AuthorityLevel required) => actual >= required;
}
