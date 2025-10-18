namespace LibRed.Pages
{
    public enum PageType
    {
        DatabaseDefinition = 0x00,
        DatabasePage = 0x01,
        TableDefinition = 0x02,
        IntermediateIndexPage = 0x03,
        LeafIndexPage = 0x04,
        PageUsageBitmap = 0x05,

    }
}