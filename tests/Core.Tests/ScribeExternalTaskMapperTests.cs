using Scribe.Core;

namespace Scribe.Core.Tests;

public class ScribeExternalTaskMapperTests
{
    [Fact]
    public void TitleAndBody_ProducesTaskWithNestedNote()
    {
        var blocks = ScribeExternalTaskMapper.MapToBlocks("Look at the notice board", "Bring 3 logs", null);

        Assert.Equal(2, blocks.Count);
        Assert.Equal(ScribeBlockKind.Task, blocks[0].Kind);
        Assert.Equal("Look at the notice board", blocks[0].Text);
        Assert.Equal(0, blocks[0].Depth);
        Assert.Equal(ScribeBlockKind.Text, blocks[1].Kind);
        Assert.Equal("Bring 3 logs", blocks[1].Text);
        Assert.Equal(1, blocks[1].Depth);
    }

    [Fact]
    public void TitleOnly_ProducesSingleTopLevelTask()
    {
        var blocks = ScribeExternalTaskMapper.MapToBlocks("Look at the notice board", null, null);

        Assert.Single(blocks);
        Assert.Equal(ScribeBlockKind.Task, blocks[0].Kind);
        Assert.Equal("Look at the notice board", blocks[0].Text);
        Assert.Equal(0, blocks[0].Depth);
    }

    [Fact]
    public void BodyOnly_PromotesToStandaloneTopLevelNote()
    {
        var blocks = ScribeExternalTaskMapper.MapToBlocks(null, "Bring 3 logs", null);

        Assert.Single(blocks);
        Assert.Equal(ScribeBlockKind.Text, blocks[0].Kind);
        Assert.Equal("Bring 3 logs", blocks[0].Text);
        Assert.Equal(0, blocks[0].Depth);
    }

    [Fact]
    public void BothEmpty_ProducesNoBlocks()
    {
        var blocks = ScribeExternalTaskMapper.MapToBlocks(null, null, null);

        Assert.Empty(blocks);
    }

    [Fact]
    public void ExtraInfo_AttachesOnlyToTheDepthZeroBlock()
    {
        var withTitle = ScribeExternalTaskMapper.MapToBlocks("Title", "Body", "from NoticeBoard");
        Assert.Equal("from NoticeBoard", withTitle[0].ExtraInfo);
        Assert.Null(withTitle[1].ExtraInfo); // the nested Depth-1 note never carries it

        var bodyOnly = ScribeExternalTaskMapper.MapToBlocks(null, "Body", "from NoticeBoard");
        Assert.Equal("from NoticeBoard", bodyOnly[0].ExtraInfo);
    }

    [Fact]
    public void OverLengthInputs_AreClippedNotRejected()
    {
        string longTitle = new string('a', ScribeDocumentCodec.MaxTaskTextLength + 50);
        string longBody = new string('b', ScribeDocumentCodec.MaxTextLength + 50);
        string longExtraInfo = new string('c', ScribeDocumentCodec.MaxTaskTextLength + 50);

        var blocks = ScribeExternalTaskMapper.MapToBlocks(longTitle, longBody, longExtraInfo);

        Assert.Equal(ScribeDocumentCodec.MaxTaskTextLength, blocks[0].Text.Length);
        Assert.Equal(ScribeDocumentCodec.MaxTextLength, blocks[1].Text.Length);
        Assert.Equal(ScribeDocumentCodec.MaxTaskTextLength, blocks[0].ExtraInfo!.Length);
    }
}
