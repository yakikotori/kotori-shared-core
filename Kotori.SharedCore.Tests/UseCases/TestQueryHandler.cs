using Kotori.SharedCore.UseCases;

namespace Kotori.SharedCore.Tests.UseCases;

public class TestQueryHandler : IQueryHandler<TestQuery, string>
{
    private readonly Dictionary<string, string> _data;

    public TestQueryHandler(Dictionary<string, string> data)
    {
        _data = data;
    }

    public Task<string> ExecuteAsync(TestQuery query)
        => Task.FromResult(_data[query.Text]);
}