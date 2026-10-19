using Scribe.Core;

namespace Scribe.Core.Tests;

// Tests for the pin + settings codec. Round-trips must be exact; malformed input must fail safe.
public class ScribePinCodecTests
{
    private static ScribePinnedRef Pin(string text = "Find copper", bool done = false, bool orphaned = false,
        ScribeBlockKind kind = ScribeBlockKind.Task, string? linkTarget = null,
        string? targetItemCode = null, int targetQuantity = 1, int currentQuantity = 0,
        string? linkLabel = null, int depth = 0, bool isAcceptedAssignment = false,
        string assignerUid = "", string assignedDate = "", string? acceptedDate = null,
        string? extraInfo = null, double? assignedTimestamp = null, double? acceptedTimestamp = null) => new()
    {
        OwnerDocId = Guid.NewGuid(),
        TaskId = Guid.NewGuid(),
        PinnedAtTotalHours = 1234.5,
        Orphaned = orphaned,
        LastKnownText = text,
        LastKnownDone = done,
        Kind = kind,
        LinkTarget = linkTarget,
        TargetItemCode = targetItemCode,
        TargetQuantity = targetQuantity,
        CurrentQuantity = currentQuantity,
        LinkLabel = linkLabel,
        Depth = depth,
        IsAcceptedAssignment = isAcceptedAssignment,
        AssignerUid = assignerUid,
        AssignedDate = assignedDate,
        AcceptedDate = acceptedDate,
        ExtraInfo = extraInfo,
        AssignedTimestamp = assignedTimestamp,
        AcceptedTimestamp = acceptedTimestamp,
    };

    private static void AssertPinEqual(ScribePinnedRef expected, ScribePinnedRef actual)
    {
        Assert.Equal(expected.OwnerDocId, actual.OwnerDocId);
        Assert.Equal(expected.TaskId, actual.TaskId);
        Assert.Equal(expected.PinnedAtTotalHours, actual.PinnedAtTotalHours);
        Assert.Equal(expected.Orphaned, actual.Orphaned);
        Assert.Equal(expected.LastKnownText, actual.LastKnownText);
        Assert.Equal(expected.LastKnownDone, actual.LastKnownDone);
        Assert.Equal(expected.Kind, actual.Kind);
        Assert.Equal(expected.LinkTarget, actual.LinkTarget);
        Assert.Equal(expected.TargetItemCode, actual.TargetItemCode);
        Assert.Equal(expected.TargetQuantity, actual.TargetQuantity);
        Assert.Equal(expected.CurrentQuantity, actual.CurrentQuantity);
        Assert.Equal(expected.LinkLabel, actual.LinkLabel);
        Assert.Equal(expected.Depth, actual.Depth);
        Assert.Equal(expected.IsAcceptedAssignment, actual.IsAcceptedAssignment);
        Assert.Equal(expected.AssignerUid, actual.AssignerUid);
        Assert.Equal(expected.AssignedDate, actual.AssignedDate);
        Assert.Equal(expected.AcceptedDate, actual.AcceptedDate);
        Assert.Equal(expected.ExtraInfo, actual.ExtraInfo);
        Assert.Equal(expected.AssignedTimestamp, actual.AssignedTimestamp);
        Assert.Equal(expected.AcceptedTimestamp, actual.AcceptedTimestamp);
    }

    // ---- SPIN: list round-trip ----

    [Fact]
    public void List_RoundTrip_PreservesAllFields()
    {
        var pins = new List<ScribePinnedRef> { Pin("A", done: true), Pin("B", orphaned: true) };

        byte[] bytes = ScribePinCodec.SerializeList(pins);
        bool ok = ScribePinCodec.TryDeserializeList(bytes, out var restored);

        Assert.True(ok);
        Assert.NotNull(restored);
        Assert.Equal(2, restored!.Count);
        AssertPinEqual(pins[0], restored[0]);
        AssertPinEqual(pins[1], restored[1]);
    }

    // ---- v2: Kind + LinkTarget ----

    [Fact]
    public void List_RoundTrip_PreservesKindAndLinkTarget()
    {
        // A Link pin carries its target; a Tracker pin carries its kind but no link target; a plain Task
        // pin round-trips with the defaults — all three must survive the v2 layout exactly.
        var pins = new List<ScribePinnedRef>
        {
            Pin("See copper", kind: ScribeBlockKind.Link, linkTarget: "game:ingot-copper"),
            Pin("Gather flax", kind: ScribeBlockKind.Tracker),
            Pin("Plain task"),
        };

        byte[] bytes = ScribePinCodec.SerializeList(pins);
        bool ok = ScribePinCodec.TryDeserializeList(bytes, out var restored);

        Assert.True(ok);
        Assert.NotNull(restored);
        Assert.Equal(3, restored!.Count);
        AssertPinEqual(pins[0], restored[0]);
        AssertPinEqual(pins[1], restored[1]);
        AssertPinEqual(pins[2], restored[2]);
    }

    [Fact]
    public void TryDeserialize_V1Bytes_KindAndLinkTarget_AreUpgraded()
    {
        // Hand-build a v1 blob (no Kind/LinkTarget); migration must default them.
        var docId = Guid.NewGuid();
        var taskId = Guid.NewGuid();
        using var ms = new MemoryStream();
        using (var w = new BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            w.Write("SPIN"u8.ToArray());
            w.Write((byte)1);          // v1
            w.Write(1);                // one pin
            w.Write(docId.ToByteArray());
            w.Write(taskId.ToByteArray());
            w.Write(999.0);            // PinnedAtTotalHours
            w.Write(false);            // Orphaned
            w.Write(true);             // LastKnownDone
            w.Write("Old pin");        // LastKnownText — v1 ends here, no Kind/LinkTarget
        }

        bool ok = ScribePinCodec.TryDeserializeList(ms.ToArray(), out var restored);

        Assert.True(ok);
        Assert.NotNull(restored);
        var pin = Assert.Single(restored!);
        Assert.Equal(docId, pin.OwnerDocId);
        Assert.Equal(taskId, pin.TaskId);
        Assert.Equal("Old pin", pin.LastKnownText);
        Assert.True(pin.LastKnownDone);
        Assert.Equal(ScribeBlockKind.Task, pin.Kind);   // defaulted by ApplyPreV2Defaults
        Assert.Null(pin.LinkTarget);                    // defaulted by ApplyPreV2Defaults
        Assert.Null(pin.TargetItemCode);                // v3 Tracker fields also default for a v1 blob
        Assert.Equal(1, pin.TargetQuantity);
        Assert.Equal(0, pin.CurrentQuantity);
    }

    // ---- v3: Tracker snapshot fields ----

    [Fact]
    public void List_RoundTrip_PreservesTrackerFields()
    {
        // A Tracker pin carries its target item code + target/current quantities; a Link and a plain Task
        // round-trip with the Tracker fields defaulted — all must survive the v3 layout exactly.
        var pins = new List<ScribePinnedRef>
        {
            Pin("Copper", kind: ScribeBlockKind.Tracker, targetItemCode: "game:ingot-copper",
                targetQuantity: 10, currentQuantity: 3),
            Pin("See copper", kind: ScribeBlockKind.Link, linkTarget: "game:ingot-copper"),
            Pin("Plain task"),
        };

        byte[] bytes = ScribePinCodec.SerializeList(pins);
        bool ok = ScribePinCodec.TryDeserializeList(bytes, out var restored);

        Assert.True(ok);
        Assert.NotNull(restored);
        Assert.Equal(3, restored!.Count);
        AssertPinEqual(pins[0], restored[0]);
        AssertPinEqual(pins[1], restored[1]);
        AssertPinEqual(pins[2], restored[2]);
    }

    [Fact]
    public void TryDeserialize_V2Bytes_TrackerFields_AreDefaulted()
    {
        // Hand-build a v2 blob (no v3 Tracker fields); the progressive read must default them
        // (TargetItemCode null, TargetQuantity 1, CurrentQuantity 0) while keeping Kind/LinkTarget.
        var docId = Guid.NewGuid();
        var taskId = Guid.NewGuid();
        using var ms = new MemoryStream();
        using (var w = new BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            w.Write("SPIN"u8.ToArray());
            w.Write((byte)2);          // v2
            w.Write(1);                // one pin
            w.Write(docId.ToByteArray());
            w.Write(taskId.ToByteArray());
            w.Write(777.0);            // PinnedAtTotalHours
            w.Write(false);            // Orphaned
            w.Write(false);            // LastKnownDone
            w.Write("Linky");          // LastKnownText
            w.Write((byte)ScribeBlockKind.Link); // Kind
            w.Write(true);             // hasLinkTarget
            w.Write("game:ingot-tin"); // LinkTarget — v2 ends here, no Tracker fields
        }

        bool ok = ScribePinCodec.TryDeserializeList(ms.ToArray(), out var restored);

        Assert.True(ok);
        Assert.NotNull(restored);
        var pin = Assert.Single(restored!);
        Assert.Equal(ScribeBlockKind.Link, pin.Kind);
        Assert.Equal("game:ingot-tin", pin.LinkTarget);
        Assert.Null(pin.TargetItemCode);                // defaulted by the progressive read
        Assert.Equal(1, pin.TargetQuantity);
        Assert.Equal(0, pin.CurrentQuantity);
    }

    // ---- v4: guide-page Link label snapshot ----

    [Fact]
    public void List_RoundTrip_PreservesLinkLabel()
    {
        // A guide-page Link pin carries its display label; an item Link, a Tracker, and a plain Task
        // round-trip with LinkLabel null — all must survive the v4 layout exactly.
        var pins = new List<ScribePinnedRef>
        {
            Pin("Knapping", kind: ScribeBlockKind.Link, linkTarget: "page:craftinginfo-knapping",
                linkLabel: "Knapping"),
            Pin("See copper", kind: ScribeBlockKind.Link, linkTarget: "game:ingot-copper"),
            Pin("Plain task"),
        };

        byte[] bytes = ScribePinCodec.SerializeList(pins);
        bool ok = ScribePinCodec.TryDeserializeList(bytes, out var restored);

        Assert.True(ok);
        Assert.NotNull(restored);
        Assert.Equal(3, restored!.Count);
        AssertPinEqual(pins[0], restored[0]);
        AssertPinEqual(pins[1], restored[1]);
        AssertPinEqual(pins[2], restored[2]);
        Assert.Equal("Knapping", restored[0].LinkLabel);
        Assert.Null(restored[1].LinkLabel);
    }

    [Fact]
    public void TryDeserialize_V3Bytes_LinkLabel_IsDefaulted()
    {
        // Hand-build a v3 blob (no v4 LinkLabel); the progressive read must default it to null.
        var docId = Guid.NewGuid();
        var taskId = Guid.NewGuid();
        using var ms = new MemoryStream();
        using (var w = new BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            w.Write("SPIN"u8.ToArray());
            w.Write((byte)3);          // v3
            w.Write(1);                // one pin
            w.Write(docId.ToByteArray());
            w.Write(taskId.ToByteArray());
            w.Write(555.0);            // PinnedAtTotalHours
            w.Write(false);            // Orphaned
            w.Write(false);            // LastKnownDone
            w.Write("Copper");         // LastKnownText
            w.Write((byte)ScribeBlockKind.Tracker); // Kind
            w.Write(false);            // hasLinkTarget
            w.Write(true);             // hasTargetItemCode
            w.Write("game:ingot-copper"); // TargetItemCode
            w.Write(10);               // TargetQuantity
            w.Write(3);                // CurrentQuantity — v3 ends here, no LinkLabel
        }

        bool ok = ScribePinCodec.TryDeserializeList(ms.ToArray(), out var restored);

        Assert.True(ok);
        Assert.NotNull(restored);
        var pin = Assert.Single(restored!);
        Assert.Equal(ScribeBlockKind.Tracker, pin.Kind);
        Assert.Equal("game:ingot-copper", pin.TargetItemCode);
        Assert.Equal(10, pin.TargetQuantity);
        Assert.Equal(3, pin.CurrentQuantity);
        Assert.Null(pin.LinkLabel);   // defaulted by the progressive read
        Assert.Equal(0, pin.Depth);   // v3 blob has no v5 Depth → defaulted to 0
    }

    // ---- v5: subtask depth snapshot ----

    [Fact]
    public void List_RoundTrip_PreservesDepth()
    {
        // A pinned subtask (a Craft ingredient child) carries Depth 1; a top-level pin carries 0. Both must
        // survive the v5 layout exactly.
        var pins = new List<ScribePinnedRef>
        {
            Pin("Craft axe", kind: ScribeBlockKind.Craft, targetItemCode: "game:axe-copper",
                targetQuantity: 1, depth: 0),
            Pin("game:stick", kind: ScribeBlockKind.Tracker, targetItemCode: "game:stick",
                targetQuantity: 2, depth: 1),
        };

        byte[] bytes = ScribePinCodec.SerializeList(pins);
        bool ok = ScribePinCodec.TryDeserializeList(bytes, out var restored);

        Assert.True(ok);
        Assert.NotNull(restored);
        Assert.Equal(2, restored!.Count);
        AssertPinEqual(pins[0], restored[0]);
        AssertPinEqual(pins[1], restored[1]);
        Assert.Equal(0, restored[0].Depth);
        Assert.Equal(1, restored[1].Depth);
    }

    [Fact]
    public void TryDeserialize_V4Bytes_Depth_IsDefaulted()
    {
        // Hand-build a v4 blob (no v5 Depth); the progressive read must default Depth to 0.
        var docId = Guid.NewGuid();
        var taskId = Guid.NewGuid();
        using var ms = new MemoryStream();
        using (var w = new BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            w.Write("SPIN"u8.ToArray());
            w.Write((byte)4);          // v4
            w.Write(1);                // one pin
            w.Write(docId.ToByteArray());
            w.Write(taskId.ToByteArray());
            w.Write(555.0);            // PinnedAtTotalHours
            w.Write(false);            // Orphaned
            w.Write(false);            // LastKnownDone
            w.Write("Copper");         // LastKnownText
            w.Write((byte)ScribeBlockKind.Tracker); // Kind
            w.Write(false);            // hasLinkTarget
            w.Write(true);             // hasTargetItemCode
            w.Write("game:ingot-copper"); // TargetItemCode
            w.Write(10);               // TargetQuantity
            w.Write(3);                // CurrentQuantity
            w.Write(false);            // hasLinkLabel — v4 ends here, no Depth
        }

        bool ok = ScribePinCodec.TryDeserializeList(ms.ToArray(), out var restored);

        Assert.True(ok);
        Assert.NotNull(restored);
        var pin = Assert.Single(restored!);
        Assert.Equal(ScribeBlockKind.Tracker, pin.Kind);
        Assert.Equal(3, pin.CurrentQuantity);
        Assert.Equal(0, pin.Depth);   // defaulted by the progressive read
    }

    [Fact]
    public void TryDeserialize_V5Bytes_ClampsDepthToOneLevel()
    {
        // A hostile/malformed v5 blob claiming Depth 5 must clamp to the one-level subtask contract [0,1]
        // on read, so a depth-2+ pin can never enter the live set.
        var docId = Guid.NewGuid();
        var taskId = Guid.NewGuid();
        using var ms = new MemoryStream();
        using (var w = new BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            w.Write("SPIN"u8.ToArray());
            w.Write((byte)5);          // v5
            w.Write(1);                // one pin
            w.Write(docId.ToByteArray());
            w.Write(taskId.ToByteArray());
            w.Write(555.0);            // PinnedAtTotalHours
            w.Write(false);            // Orphaned
            w.Write(false);            // LastKnownDone
            w.Write("Copper");         // LastKnownText
            w.Write((byte)ScribeBlockKind.Task); // Kind
            w.Write(false);            // hasLinkTarget
            w.Write(false);            // hasTargetItemCode
            w.Write(1);                // TargetQuantity
            w.Write(0);                // CurrentQuantity
            w.Write(false);            // hasLinkLabel
            w.Write(5);                // Depth — out of range, must clamp to 1
        }

        bool ok = ScribePinCodec.TryDeserializeList(ms.ToArray(), out var restored);

        Assert.True(ok);
        Assert.NotNull(restored);
        Assert.Equal(1, Assert.Single(restored!).Depth);
    }

    // ---- v6: accepted-assignment marker ----

    [Fact]
    public void List_RoundTrip_PreservesIsAcceptedAssignment()
    {
        var pins = new List<ScribePinnedRef>
        {
            Pin("Deliver 10 logs", isAcceptedAssignment: true),
            Pin("Ordinary task", isAcceptedAssignment: false),
        };

        byte[] bytes = ScribePinCodec.SerializeList(pins);
        bool ok = ScribePinCodec.TryDeserializeList(bytes, out var restored);

        Assert.True(ok);
        Assert.NotNull(restored);
        Assert.Equal(2, restored!.Count);
        AssertPinEqual(pins[0], restored[0]);
        AssertPinEqual(pins[1], restored[1]);
        Assert.True(restored[0].IsAcceptedAssignment);
        Assert.False(restored[1].IsAcceptedAssignment);
    }

    [Fact]
    public void TryDeserialize_V5Bytes_IsAcceptedAssignment_IsDefaulted()
    {
        // Hand-build a v5 blob (no v6 IsAcceptedAssignment); the progressive read must default it to false.
        var docId = Guid.NewGuid();
        var taskId = Guid.NewGuid();
        using var ms = new MemoryStream();
        using (var w = new BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            w.Write("SPIN"u8.ToArray());
            w.Write((byte)5);          // v5
            w.Write(1);                // one pin
            w.Write(docId.ToByteArray());
            w.Write(taskId.ToByteArray());
            w.Write(555.0);            // PinnedAtTotalHours
            w.Write(false);            // Orphaned
            w.Write(false);            // LastKnownDone
            w.Write("Copper");         // LastKnownText
            w.Write((byte)ScribeBlockKind.Task); // Kind
            w.Write(false);            // hasLinkTarget
            w.Write(false);            // hasTargetItemCode
            w.Write(1);                // TargetQuantity
            w.Write(0);                // CurrentQuantity
            w.Write(false);            // hasLinkLabel
            w.Write(0);                // Depth — v5 ends here, no IsAcceptedAssignment
        }

        bool ok = ScribePinCodec.TryDeserializeList(ms.ToArray(), out var restored);

        Assert.True(ok);
        Assert.NotNull(restored);
        Assert.False(Assert.Single(restored!).IsAcceptedAssignment); // defaulted by the progressive read
    }

    // ---- v7: assignment marker tooltip provenance ----

    [Fact]
    public void List_RoundTrip_PreservesAssignmentProvenance()
    {
        var pins = new List<ScribePinnedRef>
        {
            Pin("Deliver 10 logs", isAcceptedAssignment: true, assignerUid: "player-123",
                assignedDate: "8 August, Year 0", acceptedDate: "9 August, Year 0"),
            Pin("Ordinary task", isAcceptedAssignment: false),
        };

        byte[] bytes = ScribePinCodec.SerializeList(pins);
        bool ok = ScribePinCodec.TryDeserializeList(bytes, out var restored);

        Assert.True(ok);
        Assert.NotNull(restored);
        Assert.Equal(2, restored!.Count);
        AssertPinEqual(pins[0], restored[0]);
        AssertPinEqual(pins[1], restored[1]);
        Assert.Equal("player-123", restored[0].AssignerUid);
        Assert.Equal("8 August, Year 0", restored[0].AssignedDate);
        Assert.Equal("9 August, Year 0", restored[0].AcceptedDate);
        Assert.Equal("", restored[1].AssignerUid);
        Assert.Equal("", restored[1].AssignedDate);
        Assert.Null(restored[1].AcceptedDate);
    }

    [Fact]
    public void TryDeserialize_V6Bytes_AssignmentProvenance_IsDefaulted()
    {
        // Hand-build a v6 blob (no v7 provenance); the progressive read must default the new fields.
        var docId = Guid.NewGuid();
        var taskId = Guid.NewGuid();
        using var ms = new MemoryStream();
        using (var w = new BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            w.Write("SPIN"u8.ToArray());
            w.Write((byte)6);          // v6
            w.Write(1);                // one pin
            w.Write(docId.ToByteArray());
            w.Write(taskId.ToByteArray());
            w.Write(555.0);            // PinnedAtTotalHours
            w.Write(false);            // Orphaned
            w.Write(false);            // LastKnownDone
            w.Write("Copper");         // LastKnownText
            w.Write((byte)ScribeBlockKind.Task); // Kind
            w.Write(false);            // hasLinkTarget
            w.Write(false);            // hasTargetItemCode
            w.Write(1);                // TargetQuantity
            w.Write(0);                // CurrentQuantity
            w.Write(false);            // hasLinkLabel
            w.Write(0);                // Depth
            w.Write(true);             // IsAcceptedAssignment — v6 ends here, no provenance fields
        }

        bool ok = ScribePinCodec.TryDeserializeList(ms.ToArray(), out var restored);

        Assert.True(ok);
        Assert.NotNull(restored);
        var pin = Assert.Single(restored!);
        Assert.True(pin.IsAcceptedAssignment);
        Assert.Equal("", pin.AssignerUid);      // defaulted by the progressive read
        Assert.Equal("", pin.AssignedDate);
        Assert.Null(pin.AcceptedDate);
    }

    [Fact]
    public void List_RoundTrip_PreservesExtraInfo()
    {
        var pins = new List<ScribePinnedRef>
        {
            Pin("Check the notice board", extraInfo: "posted by NoticeBoard"),
            Pin("Ordinary task"),
        };

        byte[] bytes = ScribePinCodec.SerializeList(pins);
        bool ok = ScribePinCodec.TryDeserializeList(bytes, out var restored);

        Assert.True(ok);
        Assert.NotNull(restored);
        AssertPinEqual(pins[0], restored![0]);
        AssertPinEqual(pins[1], restored[1]);
        Assert.Equal("posted by NoticeBoard", restored[0].ExtraInfo);
        Assert.Null(restored[1].ExtraInfo);
    }

    [Fact]
    public void TryDeserialize_V7Bytes_ExtraInfo_IsDefaulted()
    {
        // Hand-build a v7 blob (no v8 ExtraInfo); the progressive read must default it to null.
        var docId = Guid.NewGuid();
        var taskId = Guid.NewGuid();
        using var ms = new MemoryStream();
        using (var w = new BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            w.Write("SPIN"u8.ToArray());
            w.Write((byte)7);          // v7
            w.Write(1);                // one pin
            w.Write(docId.ToByteArray());
            w.Write(taskId.ToByteArray());
            w.Write(555.0);            // PinnedAtTotalHours
            w.Write(false);            // Orphaned
            w.Write(false);            // LastKnownDone
            w.Write("Copper");         // LastKnownText
            w.Write((byte)ScribeBlockKind.Task); // Kind
            w.Write(false);            // hasLinkTarget
            w.Write(false);            // hasTargetItemCode
            w.Write(1);                // TargetQuantity
            w.Write(0);                // CurrentQuantity
            w.Write(false);            // hasLinkLabel
            w.Write(0);                // Depth
            w.Write(false);            // IsAcceptedAssignment
            w.Write("");               // AssignerUid
            w.Write("");               // AssignedDate
            w.Write(false);            // hasAcceptedDate — v7 ends here, no ExtraInfo field
        }

        bool ok = ScribePinCodec.TryDeserializeList(ms.ToArray(), out var restored);

        Assert.True(ok);
        Assert.NotNull(restored);
        var pin = Assert.Single(restored!);
        Assert.Null(pin.ExtraInfo); // defaulted by the progressive read
    }

    [Fact]
    public void TryDeserialize_V8Bytes_Timestamps_AreDefaulted()
    {
        var docId = Guid.NewGuid();
        var taskId = Guid.NewGuid();
        using var ms = new MemoryStream();
        using (var w = new BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            w.Write("SPIN"u8.ToArray());
            w.Write((byte)8);
            w.Write(1);
            w.Write(docId.ToByteArray());
            w.Write(taskId.ToByteArray());
            w.Write(555.0);
            w.Write(false);
            w.Write(false);
            w.Write("Copper");
            w.Write((byte)ScribeBlockKind.Task);
            w.Write(false);
            w.Write(false);
            w.Write(1);
            w.Write(0);
            w.Write(false);
            w.Write(0);
            w.Write(true);             // IsAcceptedAssignment
            w.Write("assigner-uid");
            w.Write("Year 1, Day 1");
            w.Write(true);             // hasAcceptedDate
            w.Write("Year 1, Day 2");
            w.Write(false);            // hasExtraInfo — v8 ends here, no timestamps
        }

        bool ok = ScribePinCodec.TryDeserializeList(ms.ToArray(), out var restored);

        Assert.True(ok);
        var pin = Assert.Single(restored!);
        Assert.Equal("Year 1, Day 1", pin.AssignedDate);
        Assert.Equal("Year 1, Day 2", pin.AcceptedDate);
        Assert.Null(pin.AssignedTimestamp);
        Assert.Null(pin.AcceptedTimestamp);
    }

    [Fact]
    public void List_RoundTrip_PreservesTimestampsIncludingZero()
    {
        var pins = new List<ScribePinnedRef>
        {
            Pin("Zero day", isAcceptedAssignment: true, assignerUid: "a", assignedDate: "Day 1",
                acceptedDate: "Day 2", assignedTimestamp: 0d, acceptedTimestamp: 3.25),
            Pin("Baked only", isAcceptedAssignment: true, assignerUid: "a", assignedDate: "Day 1",
                acceptedDate: "Day 2"),
        };

        byte[] bytes = ScribePinCodec.SerializeList(pins);
        bool ok = ScribePinCodec.TryDeserializeList(bytes, out var restored);

        Assert.True(ok);
        AssertPinEqual(pins[0], restored![0]);
        AssertPinEqual(pins[1], restored[1]);
        Assert.Equal(0d, restored[0].AssignedTimestamp);
        Assert.Equal(3.25, restored[0].AcceptedTimestamp);
        Assert.Null(restored[1].AssignedTimestamp);
        Assert.Null(restored[1].AcceptedTimestamp);
    }

    [Fact]
    public void TryDeserialize_V10Bytes_FailSafesFalse()
    {
        var pins = new List<ScribePinnedRef> { Pin("A") };
        byte[] bytes = ScribePinCodec.SerializeList(pins);
        bytes[4] = 10;

        Assert.False(ScribePinCodec.TryDeserializeList(bytes, out var restored));
        Assert.Null(restored);
    }

    [Fact]
    public void List_RoundTrip_Empty()
    {
        byte[] bytes = ScribePinCodec.SerializeList(new List<ScribePinnedRef>());
        bool ok = ScribePinCodec.TryDeserializeList(bytes, out var restored);

        Assert.True(ok);
        Assert.NotNull(restored);
        Assert.Empty(restored!);
    }

    // ---- SPST: store round-trip ----

    [Fact]
    public void Store_RoundTrip_PreservesPerPlayerSets()
    {
        var store = new Dictionary<string, List<ScribePinnedRef>>
        {
            ["player-1"] = new() { Pin("A") },
            ["player-2"] = new() { Pin("B"), Pin("C") },
        };

        byte[] bytes = ScribePinCodec.SerializeStore(store);
        bool ok = ScribePinCodec.TryDeserializeStore(bytes, out var restored);

        Assert.True(ok);
        Assert.NotNull(restored);
        Assert.Equal(2, restored!.Count);
        Assert.Single(restored["player-1"]);
        Assert.Equal(2, restored["player-2"].Count);
        AssertPinEqual(store["player-2"][1], restored["player-2"][1]);
    }

    // ---- Player-preference defaults + normalize guards ----
    // Settings persist as client-local JSON, not a codec blob; only defaults and normalize guards live in Core.

    [Fact]
    public void Settings_DefaultInstance_SinksAndShowsThreeRows()
    {
        // A player who never changed a setting: Sink policy, HUD not collapsed, 3 rows.
        var settings = new ScribePlayerSettings();

        Assert.Equal(ScribeCompletionPolicy.Sink, settings.CompletionPolicy);
        Assert.False(settings.HudCollapsed);
        Assert.Equal(3, settings.HudMaxRows);
    }

    [Theory]
    [InlineData(0, 1)]                 // below min → clamped up
    [InlineData(-5, 1)]                // negative → clamped up
    [InlineData(1000, 30)]           // above max → clamped down (max raised 10 → 30)
    [InlineData(31, 30)]             // just above the new max
    [InlineData(11, 11)]             // previously re-clamped to 10; 11–30 now stick
    [InlineData(30, 30)]
    [InlineData(5, 5)]                 // in range → unchanged
    public void Settings_ClampHudMaxRows_BoundsTheValue(int stored, int expected)
    {
        Assert.Equal(expected, ScribePlayerSettings.ClampHudMaxRows(stored));
    }

    [Fact]
    public void Settings_NormalizePolicy_FallsUnknownBackToSink()
    {
        // A defined value passes through; an undefined enum value (cast from a hand-broken JSON) falls
        // back to the safe non-destructive default.
        Assert.Equal(ScribeCompletionPolicy.Delete,
            ScribePlayerSettings.NormalizePolicy(ScribeCompletionPolicy.Delete));
        Assert.Equal(ScribeCompletionPolicy.Sink,
            ScribePlayerSettings.NormalizePolicy((ScribeCompletionPolicy)99));
    }

    [Fact]
    public void Settings_Normalized_ClampsRowsAndPolicyInPlace()
    {
        var settings = new ScribePlayerSettings
        {
            HudMaxRows = 1000,
            CompletionPolicy = (ScribeCompletionPolicy)99,
            HudCollapsed = true,
        };

        var result = settings.Normalized();

        Assert.Same(settings, result);                                   // mutates + returns this
        Assert.Equal(ScribePlayerSettings.MaxHudMaxRows, settings.HudMaxRows);
        Assert.Equal(ScribeCompletionPolicy.Sink, settings.CompletionPolicy);
        Assert.True(settings.HudCollapsed);                              // untouched field preserved
    }

    // ---- HUD position preferences ----

    [Fact]
    public void Settings_DefaultInstance_AnchorsTopLeftAt250Wide()
    {
        var settings = new ScribePlayerSettings();

        Assert.Equal(ScribeHudAnchor.TopLeft, settings.HudAnchor);
        Assert.Equal(250, settings.HudRowWidth);
        Assert.Equal(0, settings.HudOffsetX);
        Assert.Equal(0, settings.HudOffsetY);
    }

    [Fact]
    public void Settings_NormalizeAnchor_FallsUnknownBackToTopLeft()
    {
        Assert.Equal(ScribeHudAnchor.BottomLeft,
            ScribePlayerSettings.NormalizeAnchor(ScribeHudAnchor.BottomLeft));
        Assert.Equal(ScribeHudAnchor.TopLeft,
            ScribePlayerSettings.NormalizeAnchor((ScribeHudAnchor)99));
    }

    [Theory]
    [InlineData(0, ScribePlayerSettings.MinHudRowWidth)]        // below min → clamped up
    [InlineData(-40, ScribePlayerSettings.MinHudRowWidth)]      // negative → clamped up
    [InlineData(5000, ScribePlayerSettings.MaxHudRowWidth)]     // above max → clamped down
    [InlineData(300, 300)]                                      // in range → unchanged
    public void Settings_ClampHudRowWidth_BoundsTheValue(int stored, int expected)
    {
        Assert.Equal(expected, ScribePlayerSettings.ClampHudRowWidth(stored));
    }

    [Fact]
    public void Settings_Normalized_ClampsWidthAndAnchorInPlace()
    {
        var settings = new ScribePlayerSettings
        {
            HudAnchor = (ScribeHudAnchor)99,
            HudRowWidth = 5000,
            HudOffsetX = -37,   // offsets are free-form nudges; not clamped
            HudOffsetY = 42,
        };

        settings.Normalized();

        Assert.Equal(ScribeHudAnchor.TopLeft, settings.HudAnchor);
        Assert.Equal(ScribePlayerSettings.MaxHudRowWidth, settings.HudRowWidth);
        Assert.Equal(-37, settings.HudOffsetX);   // untouched
        Assert.Equal(42, settings.HudOffsetY);    // untouched
    }

    // ---- fail-safe paths ----

    [Fact]
    public void TryDeserializeList_Null_FailsSafely()
    {
        Assert.False(ScribePinCodec.TryDeserializeList(null, out var restored));
        Assert.Null(restored);
    }

    [Fact]
    public void TryDeserializeList_Garbage_FailsSafely()
    {
        var garbage = new byte[] { 0x01, 0x02, 0x03, 0xFF, 0x7A, 0x00 };

        Assert.False(ScribePinCodec.TryDeserializeList(garbage, out var restored));
        Assert.Null(restored);
    }

    [Fact]
    public void TryDeserializeList_WrongMagic_FailsSafely()
    {
        // A blob with a different 4-byte magic is not a pin list — the magic must gate it out.
        byte[] wrongMagic = System.Text.Encoding.UTF8.GetBytes("SPSE");

        Assert.False(ScribePinCodec.TryDeserializeList(wrongMagic, out var restored));
        Assert.Null(restored);
    }

    [Fact]
    public void TryDeserializeList_Truncated_FailsSafely()
    {
        byte[] bytes = ScribePinCodec.SerializeList(new List<ScribePinnedRef> { Pin() });
        var truncated = bytes[..(bytes.Length - 4)]; // lop off the tail of the text

        Assert.False(ScribePinCodec.TryDeserializeList(truncated, out var restored));
        Assert.Null(restored);
    }

    [Fact]
    public void TryDeserializeList_OverPinCap_FailsSafely()
    {
        // Hand-build a list blob claiming more than MaxPinsPerPlayer entries.
        using var ms = new MemoryStream();
        using (var w = new BinaryWriter(ms))
        {
            w.Write("SPIN"u8.ToArray());
            w.Write((byte)1);
            w.Write(ScribePinCodec.MaxPinsPerPlayer + 1);
        }

        Assert.False(ScribePinCodec.TryDeserializeList(ms.ToArray(), out var restored));
        Assert.Null(restored);
    }

    [Fact]
    public void TryDeserializeList_OverLongText_FailsSafely()
    {
        var pin = Pin(text: new string('a', ScribeDocumentCodec.MaxTextLength + 1));
        byte[] bytes = ScribePinCodec.SerializeList(new List<ScribePinnedRef> { pin });

        Assert.False(ScribePinCodec.TryDeserializeList(bytes, out var restored));
        Assert.Null(restored);
    }
}
