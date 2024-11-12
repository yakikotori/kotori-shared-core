using Kotori.SharedCore.UseCases;

namespace Kotori.SharedCore.Tests.UseCases;

public class TestQuery : IQuery<string, string>
{
    private readonly Dictionary<string, string> _data;

    public TestQuery(Dictionary<string, string> data)
    {
        _data = data;
    }

    public Task<string> ExecuteAsync(string query)
        => Task.FromResult(_data[query]);
}