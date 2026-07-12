using Unity.Netcode.Components;

// A NetworkTransform where the OWNER of the object moves it (you move your own
// character), instead of the default where only the host may move things.
//
// This is the standard pattern from the Netcode docs for "owner-authoritative"
// movement — simple and responsive, right for a friends co-op game.
public class ClientNetworkTransform : NetworkTransform
{
    protected override bool OnIsServerAuthoritative() => false;
}
