namespace FireCalc.Api.Endpoints;

/// <summary>Collects field errors and turns them into a 400 ValidationProblem.</summary>
internal sealed class Validation
{
    private readonly Dictionary<string, string[]> _errors = [];

    public Validation Check(bool ok, string field, string message)
    {
        if (!ok) _errors[field] = [.. _errors.GetValueOrDefault(field, []), message];
        return this;
    }

    public bool IsValid => _errors.Count == 0;

    public IResult Problem() => Results.ValidationProblem(_errors);
}
