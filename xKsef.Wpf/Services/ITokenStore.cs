public interface ITokenStore
{
    string? AccessToken { get; set; }
    event EventHandler? TokenChanged;
}