﻿// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

#nullable disable

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using osu.Framework.Extensions.ObjectExtensions;
using osu.Framework.Graphics.Rendering;
using osu.Framework.Graphics.Textures;
using osu.Framework.Logging;
using osu.Framework.Platform;
using osu.Framework.Text;

namespace osu.Framework.IO.Stores
{
    public class FontStore : TextureStore, ITexturedGlyphLookupStore
    {
        private readonly List<IGlyphStore> glyphStores = new List<IGlyphStore>();

        private readonly List<ITexturedGlyphLookupStore> nestedFontStores = new List<ITexturedGlyphLookupStore>();

        private Storage cacheStorage;

        /// <summary>
        /// A local cache to avoid string allocation overhead. Can be changed to (string,char)=>string if this ever becomes an issue,
        /// but as long as we directly inherit <see cref="TextureStore"/> this is a slight optimisation.
        /// </summary>
        private readonly ConcurrentDictionary<(string, char), ITexturedCharacterGlyph> namespacedGlyphCache = new ConcurrentDictionary<(string, char), ITexturedCharacterGlyph>();

        /// <summary>
        /// Construct a font store to be added to a parent font store via <see cref="AddStore"/>.
        /// </summary>
        /// <param name="renderer">The renderer to create textures with.</param>
        /// <param name="store">The texture source.</param>
        /// <param name="scaleAdjust">The raw pixel height of the font. Can be used to apply a global scale or metric to font usages.</param>
        public FontStore(IRenderer renderer, IResourceStore<TextureUpload> store = null, float scaleAdjust = 100)
            : this(renderer, store, scaleAdjust, false)
        {
        }

        /// <summary>
        /// Construct a font store with a custom filtering mode to be added to a parent font store via <see cref="AddStore"/>.
        /// All fonts that use the specified filter mode should be nested inside this store to make optimal use of texture atlases.
        /// </summary>
        /// <param name="renderer">The renderer to create textures with.</param>
        /// <param name="store">The texture source.</param>
        /// <param name="scaleAdjust">The raw pixel height of the font. Can be used to apply a global scale or metric to font usages.</param>
        /// <param name="minFilterMode">The texture minification filtering mode to use.</param>
        public FontStore(IRenderer renderer, IResourceStore<TextureUpload> store = null, float scaleAdjust = 100, TextureFilteringMode minFilterMode = TextureFilteringMode.Linear)
            : this(renderer, store, scaleAdjust, true, filteringMode: minFilterMode)
        {
        }

        internal FontStore(IRenderer renderer, IResourceStore<TextureUpload> store = null, float scaleAdjust = 100, bool useAtlas = false, Storage cacheStorage = null,
                           TextureFilteringMode filteringMode = TextureFilteringMode.Linear)
            : base(renderer, store, scaleAdjust: scaleAdjust, useAtlas: useAtlas, filteringMode: filteringMode)
        {
            this.cacheStorage = cacheStorage;
        }

        public override void AddTextureSource(IResourceStore<TextureUpload> store)
        {
            if (store is IGlyphStore gs)
            {
                if (gs is RawCachingGlyphStore raw && raw.CacheStorage == null)
                    raw.CacheStorage = cacheStorage;

                glyphStores.Add(gs);
                queueLoad(gs);
            }

            base.AddTextureSource(store);
        }

        public override void AddStore(ITextureStore store)
        {
            switch (store)
            {
                case FontStore fs:
                    // if null, share the main store's atlas.
                    fs.Atlas ??= Atlas;
                    fs.cacheStorage ??= cacheStorage;
                    nestedFontStores.Add(fs);
                    break;

                case ITexturedGlyphLookupStore gs:
                    nestedFontStores.Add(gs);
                    break;
            }

            base.AddStore(store);
        }

        private Task childStoreLoadTasks;

        /// <summary>
        /// Append child stores to a single threaded load task.
        /// </summary>
        private void queueLoad(IGlyphStore store)
        {
            var previousLoadStream = childStoreLoadTasks;

            childStoreLoadTasks = Task.Run(async () =>
            {
                if (previousLoadStream != null)
                    await previousLoadStream.ConfigureAwait(false);

                try
                {
                    Logger.Log($"Loading Font {store.FontName}...", level: LogLevel.Debug);
                    await store.LoadFontAsync().ConfigureAwait(false);
                    Logger.Log($"Loaded Font {store.FontName}!", level: LogLevel.Debug);
                }
                catch
                {
                    // Errors are logged by LoadFontAsync() but also propagated outwards.
                    // We can gracefully continue when loading a font fails, so the exception shouldn't trigger the unobserved exception handler of GameHost and potentially crash the game.
                }
            });
        }

        public override void RemoveTextureStore(IResourceStore<TextureUpload> store)
        {
            if (store is GlyphStore gs)
                glyphStores.Remove(gs);

            base.RemoveTextureStore(store);
        }

        public override void RemoveStore(ITextureStore store)
        {
            if (store is FontStore fs)
                nestedFontStores.Remove(fs);

            base.RemoveStore(store);
        }

        public ITexturedCharacterGlyph Get(string fontName, char character)
        {
            var key = (fontName, character);

            if (namespacedGlyphCache.TryGetValue(key, out var existing))
                return existing;

            foreach (var store in glyphStores)
            {
                if (store.FontName.EndsWith(fontName ?? string.Empty, StringComparison.Ordinal) && store.HasGlyph(character))
                {
                    string textureName = $"{store.FontName}/{character}";
                    return namespacedGlyphCache[key] = new TexturedCharacterGlyph(store.Get(character).AsNonNull(), Get(textureName), 1 / ScaleAdjust);
                }
            }

            foreach (var store in nestedFontStores)
            {
                var glyph = store.Get(fontName, character);
                if (glyph != null)
                    return namespacedGlyphCache[key] = glyph;
            }

            return namespacedGlyphCache[key] = null;
        }

        public Task<ITexturedCharacterGlyph> GetAsync(string fontName, char character) => Task.Run(() => Get(fontName, character));
    }
}
