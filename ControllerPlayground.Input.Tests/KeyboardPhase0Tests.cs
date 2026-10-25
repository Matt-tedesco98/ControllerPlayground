using ControllerPlayground.Input;
using ControllerPlayground.Input.Diagnostics;

internal static class KeyboardPhase0Tests {
    public static int Run() {
        var owner = new ControllerDevice(Program.Id(1), Program.Id(1), 5, 0x045E, 1, ControllerFamily.Xbox);
        var other = owner with { Id = Program.Id(2) }; // Same VID/PID is deliberately insufficient.
        var input = new KeyboardPhase0Probe.Capture(Guid.NewGuid().ToString(), 1, owner.Id.ToString(),
            owner.RootId.ToString(), 1, "Xbox", "Accept", false, 100, true);
        var packet = new KeyboardPhase0Probe.Packet("action", 1, 100, input);
        void Check(bool value) { if (!value) throw new InvalidOperationException("Phase 0 identity-only probe assertion failed."); }
        Check(KeyboardPhase0Probe.IdentityOnlyApproval(packet, owner, new[] { owner, other }));
        Console.WriteLine("PASS phase0: comparison retains full ID without equating collector-local generations");
        Check(!KeyboardPhase0Probe.IdentityOnlyApproval(packet with { Input = input with { Id = other.Id.ToString() } }, owner, new[] { owner, other }));
        Console.WriteLine("PASS phase0: identical VID/PID cannot authorize another ID");
        Check(!KeyboardPhase0Probe.IdentityOnlyApproval(packet, owner, new[] { owner with { ConnectionGeneration = 6 } }));
        Check(!KeyboardPhase0Probe.IdentityOnlyApproval(packet with { Input = input with { Foreground = false } }, owner, new[] { owner }));
        Check(!KeyboardPhase0Probe.IdentityOnlyApproval(packet, null, new[] { owner }));
        Console.WriteLine("PASS phase0: missing owner, retired shell generation and background proposals rejected");
        var encoded = System.Text.Json.JsonSerializer.Serialize(packet);
        Check(System.Text.Json.JsonSerializer.Deserialize<KeyboardPhase0Probe.Packet>(encoded) == packet);
        Console.WriteLine("PASS phase0: probe JSON preserves full ID and action metadata");
        return 4;
    }
}
