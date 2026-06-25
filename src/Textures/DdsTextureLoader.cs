using System;
using UnityEngine;

namespace KupoUI.PR.Textures;

internal static class DdsTextureLoader
{
    private const uint DdsMagic = 0x20534444; // "DDS "
    private const uint FourCC_DXT1 = 0x31545844; // "DXT1"
    private const uint FourCC_DXT5 = 0x35545844; // "DXT5"

    internal static bool TryLoadTexture(byte[] bytes, string textureName, out Texture2D texture)
    {
        texture = null;

        if (bytes == null || bytes.Length < 128)
        {
            return false;
        }

        try
        {
            var magic = ReadUInt32(bytes, 0);
            if (magic != DdsMagic)
            {
                return false;
            }

            var headerSize = ReadUInt32(bytes, 4);
            if (headerSize != 124)
            {
                return false;
            }

            var height = (int)ReadUInt32(bytes, 12);
            var width = (int)ReadUInt32(bytes, 16);
            if (width <= 0 || height <= 0)
            {
                return false;
            }

            var mipMapCount = (int)ReadUInt32(bytes, 28);
            if (mipMapCount <= 0)
            {
                mipMapCount = 1;
            }

            var fourCC = ReadUInt32(bytes, 84);
            var rgbBitCount = ReadUInt32(bytes, 88);
            var rMask = ReadUInt32(bytes, 92);
            var gMask = ReadUInt32(bytes, 96);
            var bMask = ReadUInt32(bytes, 100);
            var aMask = ReadUInt32(bytes, 104);

            const int dataOffset = 128;
            if (bytes.Length <= dataOffset)
            {
                return false;
            }

            if (fourCC == FourCC_DXT1 || fourCC == FourCC_DXT5)
            {
                var format = fourCC == FourCC_DXT1 ? TextureFormat.DXT1 : TextureFormat.DXT5;
                texture = new Texture2D(width, height, format, mipMapCount > 1);

                var dataLength = bytes.Length - dataOffset;
                var raw = new byte[dataLength];
                Buffer.BlockCopy(bytes, dataOffset, raw, 0, dataLength);

                texture.LoadRawTextureData(raw);
                texture.Apply(mipMapCount > 1, false);
                texture.name = textureName;
                return true;
            }

            if (fourCC == 0 && rgbBitCount == 32 && rMask == 0x00ff0000 && gMask == 0x0000ff00 && bMask == 0x000000ff && aMask == 0xff000000)
            {
                var pixelBytes = width * height * 4;
                if (bytes.Length < dataOffset + pixelBytes)
                {
                    return false;
                }

                var rgba = new byte[pixelBytes];
                var src = dataOffset;
                var dst = 0;
                while (dst < pixelBytes)
                {
                    var b = bytes[src + 0];
                    var g = bytes[src + 1];
                    var r = bytes[src + 2];
                    var a = bytes[src + 3];

                    rgba[dst + 0] = r;
                    rgba[dst + 1] = g;
                    rgba[dst + 2] = b;
                    rgba[dst + 3] = a;

                    src += 4;
                    dst += 4;
                }

                texture = new Texture2D(width, height, TextureFormat.RGBA32, mipMapCount > 1);
                texture.LoadRawTextureData(rgba);
                texture.Apply(mipMapCount > 1, false);
                texture.name = textureName;
                return true;
            }

            return false;
        }
        catch
        {
            return false;
        }
    }

    private static uint ReadUInt32(byte[] data, int offset)
    {
        return (uint)(
            data[offset]
            | (data[offset + 1] << 8)
            | (data[offset + 2] << 16)
            | (data[offset + 3] << 24));
    }
}
