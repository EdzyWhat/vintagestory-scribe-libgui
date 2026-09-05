using System;
using System.Linq;
using Scribe.Core;
using Vintagestory.API.Client;   // ICoreClientAPI, GuiDialog

namespace Scribe;

// Quest objective progress engine (add-progression-framework-quest-objective-subtasks 5.4). While a
// Scribe surface is open in the read view, this keeps every QuestObjective child's live CurrentQuantity
// in sync with Progression Framework's own cached per-objective progress (never carried inventory —
// that's the Tracker/Craft engine in ScribeDialogBase.TrackerCount.cs, which a QuestObjective is
// deliberately excluded from via IsCarriedCountTracked). Reuses that file's poll-tick listener
// (trackerPollListenerId) rather than registering a second one, since both engines share the exact same
// read-view-only gating and lifetime.
public abstract partial class ScribeDialogBase
{
    /// <summary>Recount every QuestObjective from Progression Framework's cached progress and reconcile:
    /// push a changed count through the server (persist + converge) and refresh the read view once.
    /// No-op outside the read view, when the document has no QuestObjective, or for a QuestObjective
    /// whose parent Quest Link isn't a Progression Framework quest (a plain VS Quest Link has no
    /// per-objective numeric progress to read, only status — see <see cref="ScribeModSystem.TryGetQuestProgressText"/>).</summary>
    private void RecomputeQuestObjectiveProgress()
    {
        if (isEditorMode || viewMode != ScribeLecternView.Read || !IsOpened()) return;

        var objectives = host.Document.Blocks.Where(b => b.IsQuestObjective).ToList();
        if (objectives.Count == 0) return;

        bool anyChange = false;
        foreach (var block in objectives)
        {
            int childIndex = host.Document.IndexOf(block.TaskId);
            int parentIndex = host.Document.FindParentIndex(childIndex);
            if (parentIndex < 0) continue;
            var parent = host.Document.Blocks[parentIndex];
            if (!parent.IsLink || !ScribeLinkTarget.IsQuest(parent.LinkTarget)) continue;
            if (ScribeLinkTarget.QuestSource(parent.LinkTarget) != ScribeQuestSource.ProgressionFramework) continue;
            string? questCode = ScribeLinkTarget.QuestCode(parent.LinkTarget);
            if (questCode is null) continue;
            if (!modSystem.TryGetPfObjectiveProgress(questCode, out var progressByCode)) continue;
            if (block.LinkTarget is null || !progressByCode.TryGetValue(block.LinkTarget, out int progress)) continue;

            int clamped = Math.Clamp(progress, 0, block.TargetQuantity);
            if (clamped == block.CurrentQuantity) continue;

            // Optimistic local update so the read-view counter reflects the new count immediately; the
            // server echo (MarkDirty → FromTreeAttributes → RefreshReadView) supersedes it shortly.
            block.CurrentQuantity = clamped;
            anyChange = true;
            SendQuestObjectiveProgress(block.TaskId, clamped);
        }

        if (anyChange) RefreshReadView();
    }

    /// <summary>Send a QuestObjective's freshly-read progress to the server (Task 6's message). The
    /// server clamps and writes it lock-free through the owning host, then resyncs viewers.</summary>
    private void SendQuestObjectiveProgress(Guid taskId, int quantity)
    {
        capi.Network.GetChannel(ScribeModSystem.NetworkChannelName).SendPacket(new ScribeSetQuestObjectiveProgressMessage
        {
            DocId = host.Document.DocId.ToByteArray(),
            TaskId = taskId.ToByteArray(),
            Quantity = quantity,
        });
    }
}
