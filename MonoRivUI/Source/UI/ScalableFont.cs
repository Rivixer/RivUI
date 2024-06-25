﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using FreeTypeSharp;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using static FreeTypeSharp.FT;

namespace MonoRivUI;

/// <summary>
/// Represents a scalable font.
/// </summary>
public class ScalableFont : IDisposable
{
    private const int TextureDims = 1024;
    private const char BaseChar = 'A';
    private const int DpiX = 150;
    private const int DpiY = 150;

    private static readonly FreeTypeLibrary Library = new();

    private readonly FreeTypeFaceFacade face;
    private readonly List<Texture2D> textures = new();
    private readonly Dictionary<char, GlyphData> glyphDatas = new();
    private readonly string path;
    private int size;
    private uint height;

    private bool disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="ScalableFont"/> class.
    /// </summary>
    /// <param name="path">The path to the font.</param>
    /// <param name="size">The size of the font.</param>
    public unsafe ScalableFont(string path, int size)
    {
        this.path = path;
        this.size = size;

        FT_FaceRec_* facePtr;
        var error = FT_New_Face(Library.Native, (byte*)Marshal.StringToHGlobalAnsi(path), IntPtr.Zero, &facePtr);
        ThrowIfFTError(error, path);

        this.face = new FreeTypeFaceFacade(Library, facePtr);

        this.Render();
    }

    /// <summary>
    /// Finalizes an instance of the <see cref="ScalableFont"/> class.
    /// </summary>
    ~ScalableFont()
    {
        this.Dispose(false);
    }

    /// <summary>
    /// Gets or sets the size of the font.
    /// </summary>
    public int Size
    {
        get => this.size;
        set
        {
            if (this.size == value)
            {
                return;
            }

            this.size = value;
            this.Render();
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        this.Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Draws the text.
    /// </summary>
    /// <param name="text">The text to draw.</param>
    /// <param name="position">The position to draw the text at.</param>
    /// <param name="color">The color of the text.</param>
    public void DrawString(string text, Vector2 position, Color color)
    {
        this.DrawString(text, position, color, 0.0f, Vector2.Zero, 1.0f, SpriteEffects.None, 1.0f);
    }

    /// <summary>
    /// Draws the text.
    /// </summary>
    /// <param name="text">The text to draw.</param>
    /// <param name="position">The position to draw the text at.</param>
    /// <param name="color">The color of the text.</param>
    /// <param name="rotation">The rotation of the text.</param>
    /// <param name="origin">The origin of the text.</param>
    /// <param name="scale">The scale of the text.</param>
    /// <param name="effects">The effects of the text.</param>
    /// <param name="layerDepth">The layer depth of the text.</param>
    public void DrawString(string text, Vector2 position, Color color, float rotation, Vector2 origin, float scale, SpriteEffects effects, float layerDepth)
    {
        var sb = SpriteBatchController.SpriteBatch;

        Vector2 currentPosition = position;
        Vector2 currentOffset = Vector2.Zero;
        Vector2 advance = rotation == 0.0f ? Vector2.UnitX : new Vector2((float)Math.Cos(rotation), (float)Math.Sin(rotation));

        for (int i = 0; i < text.Length; i++)
        {
            char currentChar = text[i];

            GlyphData glyphData = this.glyphDatas[currentChar];
            if (glyphData.TextureIndex is int index)
            {
                Texture2D texture = this.textures[index];
                Vector2 drawOffset;
                drawOffset.X = (glyphData.Offset.X * advance.X) - (glyphData.Offset.Y * advance.Y);
                drawOffset.Y = (glyphData.Offset.X * advance.Y) + (glyphData.Offset.Y * advance.X);
                sb.Draw(texture, currentPosition + currentOffset + drawOffset, glyphData.TextureCoords, color, rotation, origin, 1.0f, effects, layerDepth);
            }

            currentPosition += glyphData.HorizontalAdvance * advance;
        }
    }

    /// <summary>
    /// Measures the size of the text.
    /// </summary>
    /// <param name="text">The text to measure.</param>
    /// <returns>The size of the text.</returns>
    public Vector2 MeasureString(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return Vector2.Zero;
        }

        var result = new Vector2(0, this.height * 16 / 9);

        foreach (char c in text)
        {
            GlyphData data = this.glyphDatas[c];
            result.X += data.HorizontalAdvance;
        }

        return result;
    }

    /// <summary>
    /// Measures the size of the text.
    /// </summary>
    /// <param name="sb">The string builder to measure.</param>
    /// <returns>The size of the text.</returns>
    public Vector2 MeasureString(StringBuilder sb)
    {
        return this.MeasureString(sb.ToString());
    }

    /// <summary>
    /// Disposes the object.
    /// </summary>
    /// <param name="disposing">A value indicating whether the object is disposing.</param>
    protected virtual void Dispose(bool disposing)
    {
        if (!this.disposed)
        {
            if (disposing)
            {
                foreach (var texture in this.textures)
                {
                    texture.Dispose();
                }

                this.textures.Clear();
            }

            this.disposed = true;
        }
    }

    private static void ThrowIfFTError(FT_Error error, string? path = null)
    {
        if (error != FT_Error.FT_Err_Ok)
        {
#if DEBUG
            string? absPath = path is null ? null : Path.GetFullPath(path);
#endif
            throw new FreeTypeException(error);
        }
    }

    private unsafe void Render()
    {
        this.textures.ForEach(x => x.Dispose());
        this.textures.Clear();
        this.glyphDatas.Clear();

        this.face.SelectCharSize(this.size, DpiX, DpiY);

        uint nextY = 0;
        GlyphData data;
        Texture2D texture;
        Vector2 currentCoords = Vector2.Zero;
        var pixelBuf = new uint[TextureDims * TextureDims];
        var result = new List<Texture2D>();

        FT_GlyphSlotRec_* glyph = this.LoadGlyph(BaseChar);
        this.height = glyph->bitmap.rows;

        for (var c = (char)0x20; c < 0x1FF; c++)
        {
            glyph = this.LoadGlyph(c);

            if (glyph->metrics.width == IntPtr.Zero || glyph->metrics.height == IntPtr.Zero)
            {
                data = new GlyphData(
                    Vector2.Zero,
                    this.face.GlyphMetricHorizontalAdvance,
                    Rectangle.Empty,
                    null);
                this.glyphDatas.Add(c, data);
                continue;
            }

            FT_Bitmap_ bitmap = glyph->bitmap;
            byte* bitmapBuf = bitmap.buffer;
            uint glyphWidth = bitmap.width;
            uint glyphHeight = bitmap.rows;

            if (currentCoords.X + glyphWidth + 2 >= TextureDims)
            {
                currentCoords.X = 0;
                currentCoords.Y += nextY;
                nextY = 0;
            }

            nextY = Math.Max(nextY, glyphHeight + 2);

            if (currentCoords.Y + glyphHeight + 2 >= TextureDims)
            {
                currentCoords.X = currentCoords.Y = 0;
                texture = new Texture2D(ScreenController.GraphicsDevice, TextureDims, TextureDims);
                texture.SetData(pixelBuf);
                this.textures.Add(texture);
                pixelBuf = new uint[TextureDims * TextureDims];
            }

            data = new GlyphData(
                new Vector2(this.face.GlyphBitmapLeft, this.height - this.face.GlyphBitmapTop),
                this.face.GlyphMetricHorizontalAdvance,
                new Rectangle((int)currentCoords.X, (int)currentCoords.Y, (int)glyphWidth, (int)glyphHeight),
                this.textures.Count);

            this.glyphDatas.Add(c, data);

            for (int y = 0; y < glyphHeight; y++)
            {
                for (int x = 0; x < glyphWidth; x++)
                {
                    byte alpha = *(bitmapBuf + x + (y * glyphWidth));
                    pixelBuf[(int)currentCoords.X + x + (((int)currentCoords.Y + y) * TextureDims)] = (uint)(alpha << 24) | 0x00ffffff;
                }
            }

            currentCoords.X += glyphWidth + 2;
        }

        texture = new Texture2D(ScreenController.GraphicsDevice, TextureDims, TextureDims);
        texture.SetData(pixelBuf);
        this.textures.Add(texture);
    }

    private unsafe FT_GlyphSlotRec_* LoadGlyph(char c)
    {
        uint glyphIndex = this.face.GetCharIndex(c);

        var error = FT_Load_Glyph(this.face.FaceRec, glyphIndex, FT_LOAD.FT_LOAD_DEFAULT);
        ThrowIfFTError(error);

        FT_GlyphSlotRec_* glyph = this.face.FaceRec->glyph;
        error = FT_Render_Glyph(glyph, FT_Render_Mode_.FT_RENDER_MODE_NORMAL);
        ThrowIfFTError(error);

        return glyph;
    }

    private readonly record struct GlyphData(
        Vector2 Offset,
        float HorizontalAdvance,
        Rectangle TextureCoords,
        int? TextureIndex);
}
