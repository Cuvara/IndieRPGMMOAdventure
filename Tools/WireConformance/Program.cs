using System;
using System.Text;
using Cuvara.Netcode.Codec;
using Cuvara.Netcode.Protocol;
using Cuvara.Netcode.Protocol.Messages;
using Cuvara.Netcode.Snapshot;
using Cuvara.Netcode.Transport;

internal static class Program
{
    private static int _failures;

    private static void Check(bool condition, string what)
    {
        if (!condition)
        {
            _failures++;
            Console.WriteLine("FAIL: " + what);
        }
        else
        {
            Console.WriteLine("ok:   " + what);
        }
    }

    private static void Eq(string actual, string expected, string what)
    {
        Check(actual == expected, what + " (got: " + actual + ")");
    }

    private static int Main()
    {
        var codec = new JsonWireCodec();

        // --- encoding ---
        Eq(Encoding.UTF8.GetString(codec.EncodeBody(MsgType.Auth, new AuthRequest { Token = "jwt.abc" })),
            "{\"type\":1,\"payload\":{\"token\":\"jwt.abc\"}}", "auth encodes like the Go struct tags");

        Eq(Encoding.UTF8.GetString(codec.EncodeBody(MsgType.EnterWorld, new EnterWorldRequest { MapId = "map_01" })),
            "{\"type\":3,\"payload\":{\"map_id\":\"map_01\"}}", "enter_world encodes");

        Eq(Encoding.UTF8.GetString(codec.EncodeBody(MsgType.Input,
                new InputMessage { Tick = 41, MoveX = 1f, MoveY = 0f })),
            "{\"type\":7,\"payload\":{\"tick\":41,\"move_x\":1,\"move_y\":0}}",
            "input omits an empty attack_target_id");

        Eq(Encoding.UTF8.GetString(codec.EncodeBody(MsgType.Input,
                new InputMessage { Tick = 42, MoveX = -0.5f, MoveY = 0.25f, AttackTargetId = "mob_3" })),
            "{\"type\":7,\"payload\":{\"tick\":42,\"move_x\":-0.5,\"move_y\":0.25,\"attack_target_id\":\"mob_3\"}}",
            "input carries an attack target");

        Eq(Encoding.UTF8.GetString(codec.EncodeBody(MsgType.Resync, new ResyncRequest())),
            "{\"type\":10,\"payload\":{}}", "resync is an empty payload");

        Eq(Encoding.UTF8.GetString(codec.EncodeBody(MsgType.Pong,
                new PongMessage { Timestamp = 1699999999999L, ServerTime = 1700000000000L })),
            "{\"type\":12,\"payload\":{\"timestamp\":1699999999999,\"server_time\":1700000000000}}",
            "pong keeps 64-bit millisecond timestamps intact");

        var nanInput = Encoding.UTF8.GetString(codec.EncodeBody(MsgType.Input,
            new InputMessage { Tick = 1, MoveX = float.NaN, MoveY = float.PositiveInfinity }));
        Eq(nanInput, "{\"type\":7,\"payload\":{\"tick\":1,\"move_x\":0,\"move_y\":0}}",
            "non-finite input is written as 0, never as invalid JSON");

        // --- decoding, bytes as the servers actually write them ---
        var authResp = codec.DecodeBody(Encoding.UTF8.GetBytes("{\"type\":2,\"payload\":{\"ok\":true,\"user_id\":\"u1\"}}"));
        Check(authResp.Type == MsgType.AuthResp, "auth_resp type");
        Check(authResp.Payload is AuthResponse a && a.Ok && a.UserId == "u1", "auth_resp fields");

        var authErr = codec.DecodeBody(Encoding.UTF8.GetBytes("{\"type\":2,\"payload\":{\"ok\":false,\"error\":\"invalid token\"}}"));
        Check(authErr.Payload is AuthResponse e && !e.Ok && e.Error == "invalid token", "auth_resp error");

        var ew = codec.DecodeBody(Encoding.UTF8.GetBytes(
            "{\"type\":4,\"payload\":{\"server_addr\":\"10.0.0.4:9000\",\"join_token\":\"tok\"}}"));
        var ewPayload = (EnterWorldResponse)ew.Payload;
        Check(ewPayload.Transport == "", "omitted transport decodes to empty");
        Check(TransportKinds.Parse(ewPayload.Transport) == TransportKind.Tcp, "empty transport means tcp");
        Check(TransportKinds.Parse("kcp") == TransportKind.Kcp, "kcp parses");

        var kick = codec.DecodeBody(Encoding.UTF8.GetBytes("{\"type\":15,\"payload\":{\"reason\":\"duplicate_login\"}}"));
        Check(kick.Type == MsgType.Kick && ((KickMessage)kick.Payload).Reason == "duplicate_login", "kick decodes");

        var bye = codec.DecodeBody(Encoding.UTF8.GetBytes("{\"type\":9,\"payload\":{\"reason\":\"server_shutdown\"}}"));
        Check(((DisconnectMessage)bye.Payload).Reason == "server_shutdown", "disconnect reason decodes");

        var byeEmpty = codec.DecodeBody(Encoding.UTF8.GetBytes("{\"type\":9,\"payload\":{}}"));
        Check(((DisconnectMessage)byeEmpty.Payload).Reason == "", "empty disconnect payload decodes");

        var byeNull = codec.DecodeBody(Encoding.UTF8.GetBytes("{\"type\":9,\"payload\":null}"));
        Check(byeNull.Type == MsgType.Disconnect, "null payload decodes");

        // Keyframe exactly as GameServer/Net/WireJson.cs writes one.
        var keyframeJson = "{\"type\":8,\"payload\":{\"tick\":128,\"ack_tick\":41,\"full\":true,\"entities\":[" +
                           "{\"id\":\"u1\",\"type\":\"player\",\"x\":12.5,\"y\":-3,\"hp\":90,\"max_hp\":100}," +
                           "{\"id\":\"mob_7\",\"type\":\"mob\",\"x\":1,\"y\":2,\"hp\":30,\"max_hp\":30}]}}";
        var snapFrame = codec.DecodeBody(Encoding.UTF8.GetBytes(keyframeJson));
        var snap = (SnapshotMessage)snapFrame.Payload;
        Check(snap.Tick == 128 && snap.AckTick == 41 && snap.Full, "keyframe header");
        Check(snap.Entities.Count == 2 && snap.Entities[0].Id == "u1" && snap.Entities[0].X == 12.5f
              && snap.Entities[0].Y == -3f && snap.Entities[0].MaxHp == 100, "keyframe entities");
        Check(snap.Removed.Count == 0, "keyframe carries no removals");

        var deltaJson = "{\"type\":8,\"payload\":{\"tick\":129,\"entities\":[],\"removed\":[\"mob_7\"]}}";
        var delta = (SnapshotMessage)codec.DecodeBody(Encoding.UTF8.GetBytes(deltaJson)).Payload;
        Check(!delta.Full && delta.AckTick == 0 && delta.Removed.Count == 1 && delta.Removed[0] == "mob_7",
            "delta with omitted ack_tick and full");

        // Unknown members must not break us: that is how the server adds a field.
        var future = codec.DecodeBody(Encoding.UTF8.GetBytes(
            "{\"type\":8,\"payload\":{\"tick\":5,\"entities\":[],\"future_field\":{\"a\":[1,2]}},\"extra\":1}"));
        Check(((SnapshotMessage)future.Payload).Tick == 5, "unknown members are skipped");

        var unmodelled = codec.DecodeBody(Encoding.UTF8.GetBytes("{\"type\":14,\"payload\":{\"ok\":true}}"));
        Check(unmodelled.Type == MsgType.TransferMapResp && unmodelled.Payload == null,
            "a type we do not model decodes to a null payload, not an error");

        Check(Throws(() => codec.DecodeBody(Encoding.UTF8.GetBytes("{\"type\":0,\"payload\":{}}"))),
            "type 0 is rejected, failing closed like both servers");
        Check(Throws(() => codec.DecodeBody(Encoding.UTF8.GetBytes("{\"type\":2,"))), "truncated JSON is rejected");
        Check(Throws(() => codec.EncodeBody(MsgType.Unspecified, null)), "type 0 cannot be encoded");

        // Escapes and non-ASCII survive a round trip through our own writer.
        const string awkward = "a\"b\\c\ndé\u0001";
        var quoted = codec.EncodeBody(MsgType.Auth, new AuthRequest { Token = awkward });
        var quotedFrame = codec.DecodeBody(quoted);
        Check(quotedFrame.Type == MsgType.Auth, "escaped payload parses back as an envelope");
        var reparsed = Cuvara.Netcode.Json.JsonParser.Parse(Encoding.UTF8.GetString(quoted));
        reparsed.TryGetMember("payload", out var quotedPayload);
        Check(quotedPayload.GetString("token") == awkward,
            "quotes, backslashes, control characters and non-ASCII round-trip");

        // --- sniffing ---
        Check(EncodingSniffer.Sniff(Encoding.UTF8.GetBytes("{\"type\":1}")) == WireEncoding.Json, "sniff json");
        Check(EncodingSniffer.Sniff(new byte[] { 0x08, 0x01 }) == WireEncoding.Protobuf, "sniff protobuf");
        Check(EncodingSniffer.Sniff(new byte[] { 0x12 }) == WireEncoding.Unknown, "sniff neither");
        Check(EncodingSniffer.Sniff(new byte[0]) == WireEncoding.Unknown, "sniff empty");

        // --- framing ---
        var header = new byte[4];
        WireFraming.WriteLength(header, 300);
        Check(header[0] == 0 && header[1] == 0 && header[2] == 1 && header[3] == 44, "length prefix is big-endian");
        Check(WireFraming.ReadLength(header) == 300, "length prefix round-trips");
        Check(!WireFraming.IsValidLength(0) && !WireFraming.IsValidLength(-1)
              && !WireFraming.IsValidLength((1 << 20) + 1) && WireFraming.IsValidLength(1 << 20),
            "length bounds match the 1 MiB cap");
        var negative = new byte[] { 0xFF, 0xFF, 0xFF, 0xFF };
        Check(!WireFraming.IsValidLength(WireFraming.ReadLength(negative)), "a high-bit length is rejected");

        // --- endpoints ---
        Check(NetworkEndpoint.Parse("10.0.0.4:9000").Port == 9000, "host:port parses");
        Check(NetworkEndpoint.Parse("[::1]:9000").Host == "::1", "bracketed ipv6 parses");
        Check(Throws(() => NetworkEndpoint.Parse("nohost")), "an address without a port is rejected");
        Check(Throws(() => NetworkEndpoint.Parse("h:0")), "port 0 is rejected");

        // --- interning ---
        var resolver = new SnapshotResolver();

        var intro = new SnapshotMessage { Tick = 1, Full = true };
        intro.Entities.Add(new EntitySnapshot { Id = "u1", Type = "player", Handle = 1, X = 1f });
        intro.Entities.Add(new EntitySnapshot { Id = "mob_2", Type = "mob", Handle = 2 });
        Check(resolver.TryResolve(intro, out var r1) && r1.Entities.Count == 2 && r1.Entities[0].Id == "u1",
            "a keyframe introduces bindings");

        var later = new SnapshotMessage { Tick = 2 };
        later.Entities.Add(new EntitySnapshot { Handle = 2, X = 5f });
        Check(resolver.TryResolve(later, out var r2) && r2.Entities[0].Id == "mob_2",
            "a handle-only mention resolves");

        var unknown = new SnapshotMessage { Tick = 3 };
        unknown.Entities.Add(new EntitySnapshot { Handle = 99 });
        Check(!resolver.TryResolve(unknown, out _), "an unknown handle refuses the whole snapshot");

        var mixed = new SnapshotMessage { Tick = 4 };
        mixed.Entities.Add(new EntitySnapshot { Id = "new", Handle = 7 });
        mixed.Entities.Add(new EntitySnapshot { Handle = 98 });
        Check(!resolver.TryResolve(mixed, out _), "a snapshot that fails late binds nothing");
        var reuse = new SnapshotMessage { Tick = 5 };
        reuse.Entities.Add(new EntitySnapshot { Handle = 7 });
        Check(!resolver.TryResolve(reuse, out _), "the aborted snapshot's binding was not recorded");

        var keyframe2 = new SnapshotMessage { Tick = 6, Full = true };
        keyframe2.Entities.Add(new EntitySnapshot { Id = "u1", Handle = 2 });
        Check(resolver.TryResolve(keyframe2, out var r3) && r3.Entities[0].Id == "u1",
            "a keyframe rebinds a reused handle to the new entity");
        var afterKeyframe = new SnapshotMessage { Tick = 7 };
        afterKeyframe.Entities.Add(new EntitySnapshot { Handle = 2 });
        Check(resolver.TryResolve(afterKeyframe, out var r4) && r4.Entities[0].Id == "u1",
            "handle 2 now means the new entity, not the pre-keyframe one");

        var stale = new SnapshotMessage { Tick = 8, Full = true };
        stale.Entities.Add(new EntitySnapshot { Handle = 2 });
        Check(!resolver.TryResolve(stale, out _),
            "a handle-only entity on a keyframe is unresolvable, never resolved against the old table");

        var plain = new SnapshotMessage { Tick = 9, Full = true };
        plain.Entities.Add(new EntitySnapshot { Id = "j1", Type = "player" });
        Check(resolver.TryResolve(plain, out var r5) && r5.Entities[0].Id == "j1",
            "a non-interned snapshot (the JSON encoding) resolves untouched");

        var nameless = new SnapshotMessage { Tick = 10 };
        nameless.Entities.Add(new EntitySnapshot());
        Check(!resolver.TryResolve(nameless, out _), "an entity with neither id nor handle is refused");

        Console.WriteLine();
        Console.WriteLine(_failures == 0 ? "ALL CHECKS PASSED" : _failures + " CHECK(S) FAILED");
        return _failures == 0 ? 0 : 1;
    }

    private static bool Throws(Action action)
    {
        try
        {
            action();
            return false;
        }
        catch (Exception)
        {
            return true;
        }
    }
}
