using System;
using System.Linq;
using Scribe.Core;
using Vintagestory.API.Client;   // ICoreClientAPI, GuiDialog

namespace Scribe;

// Quest objective progress engine: while in the read view, keeps each QuestObjective child's CurrentQuantity in sync with Progression Framework's
// cached per-objective progress (never carried inventory; IsCarriedCountTracked excludes it from the Tracker engine in ScribeDialogBase.TrackerCount.cs).
// Shares that engine's poll-tick listener (trackerPollListenerId) since gating and lifetime match.
public abstract partial class ScribeDialogBase
{
    /// <summary>Recounts every QuestObjective from Progression Framework's cached progress, pushes a changed count through the server and refreshes the read view once.
    /// No-op outside the read view, without QuestObjectives, or when the parent Quest Link isn't a Progression Framework quest (a plain VS quest has only status, see <see cref="ScribeModSystem.TryGetQuestProgressText"/>).</summary>
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

    /// <summary>Send a QuestObjective's freshly-read progress to the server (via its server message). The
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
