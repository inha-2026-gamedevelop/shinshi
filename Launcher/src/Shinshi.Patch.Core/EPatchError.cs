namespace Shinshi.Patch
{
    /// <summary> 패치 문서를 열거나 검사할 때 거절한 이유 </summary>
    public enum EPatchError
    {
        None,
        TooLarge,
        Malformed,
        UnsupportedFormat,
        KindMismatch,
        UntrustedKey,
        BadSignature,
        InvalidField,
        InvalidPath,
        DuplicatePath,
        ProductMismatch,
        ChannelMismatch,
        ReleaseMismatch,
        SequenceRollback,
        ManifestHashMismatch,
        BuildIdentityMissing,
        BuildIdentityAmbiguous,
        BootConfigMissing,
        BootConfigAmbiguous
    }
}
