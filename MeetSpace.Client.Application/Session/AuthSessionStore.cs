using MeetSpace.Client.Shared.Stores;
namespace MeetSpace.Client.App.Auth; 
public sealed class AuthSessionStore : StoreBase<AuthSessionState> 
{ 
    public AuthSessionStore() : base(AuthSessionState.Empty) 
    { 
    } 
    public void SetSession(AuthTokens tokens) 
    {
        Set(new AuthSessionState(true,
        tokens.UserId, 
        tokens.Email,
        tokens.AccessToken, 
        tokens.RefreshToken,
        tokens.ExpiresAtUtc));
    } 
    public void ClearSession() { Set(AuthSessionState.Empty); } }