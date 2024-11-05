namespace Shared;

public interface ITestGrain
{
    Task SetOrVerifyData(string data);
    Task<string?> GetData();
}
