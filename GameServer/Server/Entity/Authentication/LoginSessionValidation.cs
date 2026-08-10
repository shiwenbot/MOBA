namespace Fantasy;

public readonly struct LoginSessionValidation
{
    private LoginSessionValidation(bool isValid, long accountId)
    {
        IsValid = isValid;
        AccountId = accountId;
    }

    public bool IsValid { get; }
    public long AccountId { get; }

    public static LoginSessionValidation Invalid => new(false, 0L);

    public static LoginSessionValidation Valid(long accountId)
    {
        return new LoginSessionValidation(true, accountId);
    }
}
