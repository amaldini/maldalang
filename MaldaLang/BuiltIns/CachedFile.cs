// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.BuiltIns;

public class CachedFile
{
    public byte[] Content { get; set; }
    public string ContentType { get; set; }
    public DateTime LastModified { get; set; }
    
    public CachedFile(byte[] content, string contentType, DateTime lastModified)
    {
        Content = content;
        ContentType = contentType;
        LastModified = lastModified;
    }
}
