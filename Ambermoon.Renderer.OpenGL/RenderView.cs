﻿/*
 * GameView.cs - Implementation of a game render view
 *
 * Copyright (C) 2020-2021  Robert Schneckenhaus <robert.schneckenhaus@web.de>
 *
 * This file is part of Ambermoon.net.
 *
 * Ambermoon.net is free software: you can redistribute it and/or modify
 * it under the terms of the GNU General Public License as published by
 * the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 *
 * Ambermoon.net is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
 * GNU General Public License for more details.
 *
 * You should have received a copy of the GNU General Public License
 * along with Ambermoon.net. If not, see <http://www.gnu.org/licenses/>.
 */

using Ambermoon.Data;
using Ambermoon.Render;
using Silk.NET.OpenGL;
using System;
using System.Collections.Generic;

namespace Ambermoon.Renderer.OpenGL
{
    public delegate bool FullscreenRequestHandler(bool fullscreen);

    public class RenderView : RenderLayerFactory, IRenderView, IDisposable
    {
        bool disposed = false;
        readonly Context context;
        Rect renderDisplayArea;
        Size windowSize;
        readonly SizingPolicy sizingPolicy;
        readonly OrientationPolicy orientationPolicy;
        readonly DeviceType deviceType;
        Rotation rotation = Rotation.None;
        readonly SortedDictionary<Layer, RenderLayer> layers = new SortedDictionary<Layer, RenderLayer>();
        readonly SpriteFactory spriteFactory = null;
        readonly ColoredRectFactory coloredRectFactory = null;
        readonly Surface3DFactory surface3DFactory = null;
        readonly RenderTextFactory renderTextFactory = null;
        readonly FowFactory fowFactory = null;
        readonly Camera3D camera3D = null;
        PaletteReplacement paletteReplacement = null;
        bool fullscreen = false;
        const float VirtualAspectRatio = Global.VirtualAspectRatio;
        float sizeFactorX = 1.0f;
        float sizeFactorY = 1.0f;

        float RenderFactorX => (float)renderDisplayArea.Width / Global.VirtualScreenWidth;
        float RenderFactorY => (float)renderDisplayArea.Height / Global.VirtualScreenHeight;
        float WindowFactorX => (float)windowSize.Width / Global.VirtualScreenWidth;
        float WindowFactorY => (float)windowSize.Height / Global.VirtualScreenHeight;

#pragma warning disable 0067
        public event EventHandler Closed;
        public event EventHandler Click;
        public event EventHandler DoubleClick;
        public event EventHandler Drag;
        public event EventHandler KeyPress;
        public event EventHandler SystemKeyPress;
        public event EventHandler StopDrag;
#pragma warning restore 0067
        public FullscreenRequestHandler FullscreenRequestHandler { get; set; }

        public Rect RenderArea { get; }
        public Size MaxScreenSize { get; set; }
        public List<Size> AvailableFullscreenModes { get; set; }
        public bool IsLandscapeRatio { get; } = true;

        public ISpriteFactory SpriteFactory => spriteFactory;
        public IColoredRectFactory ColoredRectFactory => coloredRectFactory;
        public ISurface3DFactory Surface3DFactory => surface3DFactory;
        public IRenderTextFactory RenderTextFactory => renderTextFactory;
        public IFowFactory FowFactory => fowFactory;
        public ICamera3D Camera3D => camera3D;
        public IGameData GameData { get; }
        public IGraphicProvider GraphicProvider { get; }
        public ITextProcessor TextProcessor { get; }
        public Action<float> AspectProcessor { get; }

        #region Coordinate transformations

        PositionTransformation PositionTransformation => (Position position) =>
            new Position(Misc.Round(position.X * RenderFactorX), Misc.Round(position.Y * RenderFactorY));

        SizeTransformation SizeTransformation => (Size size) =>
        {
            // don't scale a dimension of 0
            int width = (size.Width == 0) ? 0 : Misc.Ceiling(size.Width * RenderFactorX);
            int height = (size.Height == 0) ? 0 : Misc.Ceiling(size.Height * RenderFactorY);

            return new Size(width, height);
        };

        #endregion


        public RenderView(IContextProvider contextProvider, IGameData gameData, IGraphicProvider graphicProvider,
            IFontProvider fontProvider, ITextProcessor textProcessor, Func<TextureAtlasManager> textureAtlasManagerProvider,
            int framebufferWidth, int framebufferHeight, Size windowSize,
            DeviceType deviceType = DeviceType.Desktop, SizingPolicy sizingPolicy = SizingPolicy.FitRatio,
            OrientationPolicy orientationPolicy = OrientationPolicy.Support180DegreeRotation)
            : base(new State(contextProvider))
        {
            AspectProcessor = UpdateAspect;
            GameData = gameData;
            GraphicProvider = graphicProvider;
            TextProcessor = textProcessor;
            RenderArea = new Rect(0, 0, framebufferWidth, framebufferHeight);
            renderDisplayArea = new Rect(RenderArea);
            this.windowSize = new Size(windowSize);
            this.sizingPolicy = sizingPolicy;
            this.orientationPolicy = orientationPolicy;
            this.deviceType = deviceType;
            IsLandscapeRatio = RenderArea.Width > RenderArea.Height;

            Resize(framebufferWidth, framebufferHeight);

            context = new Context(State, renderDisplayArea.Width, renderDisplayArea.Height, 1.0f);

            // factories
            var visibleArea = new Rect(0, 0, Global.VirtualScreenWidth, Global.VirtualScreenHeight);
            spriteFactory = new SpriteFactory(visibleArea);
            coloredRectFactory = new ColoredRectFactory(visibleArea);
            surface3DFactory = new Surface3DFactory(visibleArea);
            renderTextFactory = new RenderTextFactory(visibleArea);
            fowFactory = new FowFactory(visibleArea);

            camera3D = new Camera3D(State);

            TextureAtlasManager.RegisterFactory(new TextureAtlasBuilderFactory(State));

            var textureAtlasManager = textureAtlasManagerProvider?.Invoke();
            var palette = textureAtlasManager.CreatePalette(graphicProvider);

            foreach (var layer in Enum.GetValues<Layer>())
            {
                if (layer == Layer.None)
                    continue;

                try
                {
                    var texture = textureAtlasManager.GetOrCreate(layer)?.Texture;
                    var renderLayer = Create(layer, texture, palette);

                    if (layer != Layer.Map3DBackground && layer != Layer.Map3D && layer != Layer.Billboards3D)
                        renderLayer.Visible = true;

                    AddLayer(renderLayer);
                }
                catch (Exception ex)
                {
                    throw new AmbermoonException(ExceptionScope.Render, $"Unable to create layer '{layer}': {ex.Message}");
                }
            }
        }

        void UpdateAspect(float aspect)
        {
            context?.UpdateAspect(aspect);
        }

        public void Close()
        {
            //GameManager.Instance.GetCurrentGame()?.Close();

            Dispose();

            Closed?.Invoke(this, EventArgs.Empty);
        }

        public bool Fullscreen
        {
            get => fullscreen;
            set
            {
                if (fullscreen == value || FullscreenRequestHandler == null)
                    return;

                if (FullscreenRequestHandler(value))
                    fullscreen = value;
            }
        }

        void SetRotation(Orientation orientation)
        {
            if (deviceType == DeviceType.Desktop ||
                sizingPolicy == SizingPolicy.FitRatioKeepOrientation ||
                sizingPolicy == SizingPolicy.FitWindowKeepOrientation)
            {
                rotation = Rotation.None;
                return;
            }

            if (orientation == Orientation.Default)
                orientation = (deviceType == DeviceType.MobilePortrait) ? Orientation.PortraitTopDown : Orientation.LandscapeLeftRight;

            if (sizingPolicy == SizingPolicy.FitRatioForcePortrait ||
                sizingPolicy == SizingPolicy.FitWindowForcePortrait)
            {
                if (orientation == Orientation.LandscapeLeftRight)
                    orientation = Orientation.PortraitTopDown;
                else if (orientation == Orientation.LandscapeRightLeft)
                    orientation = Orientation.PortraitBottomUp;
            }
            else if (sizingPolicy == SizingPolicy.FitRatioForceLandscape ||
                     sizingPolicy == SizingPolicy.FitWindowForceLandscape)
            {
                if (orientation == Orientation.PortraitTopDown)
                    orientation = Orientation.LandscapeLeftRight;
                else if (orientation == Orientation.PortraitBottomUp)
                    orientation = Orientation.LandscapeRightLeft;
            }

            switch (orientation)
            {
                case Orientation.PortraitTopDown:
                    if (deviceType == DeviceType.MobilePortrait)
                        rotation = Rotation.None;
                    else
                        rotation = Rotation.Deg90;
                    break;
                case Orientation.LandscapeLeftRight:
                    if (deviceType == DeviceType.MobilePortrait)
                        rotation = Rotation.Deg270;
                    else
                        rotation = Rotation.None;
                    break;
                case Orientation.PortraitBottomUp:
                    if (deviceType == DeviceType.MobilePortrait)
                    {
                        if (orientationPolicy == OrientationPolicy.Support180DegreeRotation)
                            rotation = Rotation.Deg180;
                        else
                            rotation = Rotation.None;
                    }
                    else
                    {
                        if (orientationPolicy == OrientationPolicy.Support180DegreeRotation)
                            rotation = Rotation.Deg270;
                        else
                            rotation = Rotation.Deg90;
                    }
                    break;
                case Orientation.LandscapeRightLeft:
                    if (deviceType == DeviceType.MobilePortrait)
                    {
                        if (orientationPolicy == OrientationPolicy.Support180DegreeRotation)
                            rotation = Rotation.Deg270;
                        else
                            rotation = Rotation.Deg90;
                    }
                    else
                    {
                        if (orientationPolicy == OrientationPolicy.Support180DegreeRotation)
                            rotation = Rotation.Deg180;
                        else
                            rotation = Rotation.None;
                    }
                    break;
            }
        }

        public void Resize(int width, int height, int? windowWidth = null, int? windowHeight = null)
        {
            switch (deviceType)
            {
                default:
                case DeviceType.Desktop:
                case DeviceType.MobileLandscape:
                    Resize(width, height, Orientation.LandscapeLeftRight);
                    break;
                case DeviceType.MobilePortrait:
                    Resize(width, height, Orientation.PortraitTopDown);
                    break;
            }

            if (windowWidth != null)
                windowSize.Width = windowWidth.Value;
            if (windowHeight != null)
                windowSize.Height = windowHeight.Value;
        }

        public void Resize(int width, int height, Orientation orientation)
        {
            RenderArea.Size.Width = width;
            RenderArea.Size.Height = height;

            SetRotation(orientation);

            if (sizingPolicy == SizingPolicy.FitWindow ||
                sizingPolicy == SizingPolicy.FitWindowKeepOrientation ||
                sizingPolicy == SizingPolicy.FitWindowForcePortrait ||
                sizingPolicy == SizingPolicy.FitWindowForceLandscape)
            {
                renderDisplayArea = new Rect(0, 0, width, height);

                sizeFactorX = 1.0f;
                sizeFactorY = 1.0f;
            }
            else
            {
                float windowRatio = (float)width / height;
                float virtualRatio = VirtualAspectRatio;

                if (rotation == Rotation.Deg90 || rotation == Rotation.Deg270)
                    virtualRatio = 1.0f / virtualRatio;

                if (Misc.FloatEqual(windowRatio, virtualRatio))
                {
                    renderDisplayArea = new Rect(0, 0, width, height);
                }
                else if (windowRatio < virtualRatio)
                {
                    int newHeight = Misc.Round(width / virtualRatio);
                    renderDisplayArea = new Rect(0, (height - newHeight) / 2, width, newHeight);
                }
                else // ratio > virtualRatio
                {
                    int newWidth = Misc.Round(height * virtualRatio);
                    renderDisplayArea = new Rect((width - newWidth) / 2, 0, newWidth, height);
                }

                if (rotation == Rotation.Deg90 || rotation == Rotation.Deg270)
                {
                    sizeFactorX = (float)RenderArea.Height / renderDisplayArea.Width;
                    sizeFactorY = (float)RenderArea.Width / renderDisplayArea.Height;
                }
                else
                {
                    sizeFactorX = (float)RenderArea.Width / renderDisplayArea.Width;
                    sizeFactorY = (float)RenderArea.Height / renderDisplayArea.Height;
                }
            }

            State.Gl.Viewport(renderDisplayArea.X, renderDisplayArea.Y,
                (uint)renderDisplayArea.Width, (uint)renderDisplayArea.Height);
        }

        public void AddLayer(IRenderLayer layer)
        {
            if (!(layer is RenderLayer))
                throw new InvalidCastException("The given layer is not valid for this renderer.");

            layers.Add(layer.Layer, layer as RenderLayer);
        }

        public IRenderLayer GetLayer(Layer layer)
        {
            return layers[layer];
        }

        public void ShowLayer(Layer layer, bool show)
        {
            layers[layer].Visible = show;
        }

        bool accessViolationDetected = false;

        public void Render(FloatPosition viewportOffset)
        {
            if (disposed)
                return;

            try
            {
                context.SetRotation(rotation);

                State.Gl.Clear((uint)ClearBufferMask.ColorBufferBit | (uint)ClearBufferMask.DepthBufferBit);

                bool render3DMap = layers[Layer.Map3D].Visible;
                var viewOffset = new Position
                (
                    Util.Round((viewportOffset?.X ?? 0.0f) * renderDisplayArea.Width),
                    Util.Round((viewportOffset?.Y ?? 0.0f) * renderDisplayArea.Height)
                );

                foreach (var layer in layers)
                {
                    if (render3DMap)
                    {
                        if (layer.Key == Layer.Map3D)
                        {
                            // Setup 3D stuff
                            camera3D.Activate();
                            State.RestoreProjectionMatrix(State.ProjectionMatrix3D);
                            var mapViewArea = new Rect(Global.Map3DViewX, Global.Map3DViewY, Global.Map3DViewWidth + 1, Global.Map3DViewHeight + 1);
                            mapViewArea.Position = PositionTransformation(mapViewArea.Position);
                            mapViewArea.Size = SizeTransformation(mapViewArea.Size);
                            State.Gl.Viewport
                            (
                                renderDisplayArea.X + mapViewArea.X + viewOffset.X,
                                RenderArea.Height - (renderDisplayArea.Y + mapViewArea.Y + mapViewArea.Height) + viewOffset.Y,
                                (uint)mapViewArea.Width, (uint)mapViewArea.Height
                            );
                            State.Gl.Enable(EnableCap.CullFace);
                        }
                        else if (layer.Key == Layer.Billboards3D)
                        {
                            State.Gl.Disable(EnableCap.CullFace);
                        }
                        else if (layer.Key == Global.First2DLayer)
                        {
                            // Reset to 2D stuff
                            State.Gl.Clear((uint)ClearBufferMask.DepthBufferBit);
                            State.RestoreModelViewMatrix(Matrix4.Identity);
                            State.RestoreProjectionMatrix(State.ProjectionMatrix2D);
                            State.Gl.Viewport(renderDisplayArea.X + viewOffset.X, renderDisplayArea.Y + viewOffset.Y,
                                (uint)renderDisplayArea.Width, (uint)renderDisplayArea.Height);
                        }
                    }
                    else
                    {
                        State.Gl.Viewport(renderDisplayArea.X + viewOffset.X, renderDisplayArea.Y + viewOffset.Y,
                            (uint)renderDisplayArea.Width, (uint)renderDisplayArea.Height);
                    }

                    if (layer.Key == Layer.DrugEffect)
                    {
                        if (DrugColorComponent != null)
                            State.Gl.BlendColor(System.Drawing.Color.FromArgb(255, System.Drawing.Color.FromArgb(0x202020 |
                                (0x800000 >> (8 * (DrugColorComponent.Value % 3))))));
                        State.Gl.BlendFunc(BlendingFactor.DstColor, BlendingFactor.OneMinusConstantColor);
                    }
                    else if (layer.Key == Layer.FOW)
                    {
                        State.Gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
                    }
                    if (layer.Key == Layer.FOW || layer.Key == Layer.Effects || layer.Key == Layer.DrugEffect)
                        State.Gl.Enable(EnableCap.Blend);
                    else
                        State.Gl.Disable(EnableCap.Blend);

                    layer.Value.Render();
                }

                accessViolationDetected = false;
            }
            catch (AccessViolationException)
            {
                if (accessViolationDetected)
                    throw;

                accessViolationDetected = true;
            }
        }

        public Position GameToScreen(Position position) =>
            ViewToScreen(new Position(Misc.Round(position.X * WindowFactorX), Misc.Round(position.Y * WindowFactorY)));

        public Position ViewToScreen(Position position)
        {
            int rotatedX = Misc.Round(position.X / sizeFactorX);
            int rotatedY = Misc.Round(position.Y / sizeFactorY);
            int relX;
            int relY;

            switch (rotation)
            {
                case Rotation.None:
                default:
                    relX = rotatedX;
                    relY = rotatedY;
                    break;
                case Rotation.Deg90:
                    relX = renderDisplayArea.Width - rotatedY;
                    relY = rotatedX;
                     break;
                case Rotation.Deg180:
                    relX = renderDisplayArea.Width - rotatedX;
                    relY = renderDisplayArea.Height - rotatedY;
                    break;
                case Rotation.Deg270:
                    relX = rotatedY;
                    relY = renderDisplayArea.Height - rotatedX;
                    break;
            }

            return new Position(renderDisplayArea.X + relX, renderDisplayArea.Y + relY);
        }

        public Size ViewToScreen(Size size)
        {
            bool swapDimensions = rotation == Rotation.Deg90 || rotation == Rotation.Deg270;

            int width = (swapDimensions) ? size.Height : size.Width;
            int height = (swapDimensions) ? size.Width : size.Height;

            return new Size(Misc.Round(width / sizeFactorX), Misc.Round(height / sizeFactorY));
        }

        public Size GameToScreen(Size size)
        {
            bool swapDimensions = rotation == Rotation.Deg90 || rotation == Rotation.Deg270;

            int width = (swapDimensions) ? size.Height : size.Width;
            int height = (swapDimensions) ? size.Width : size.Height;

            return new Size(Misc.Round(width * WindowFactorX / sizeFactorX), Misc.Round(height * WindowFactorY / sizeFactorY));
        }

        public Rect GameToScreen(Rect rect)
        {
            var position = GameToScreen(rect.Position);
            var size = GameToScreen(rect.Size);
            rect = new Rect(position, size);

            rect.Clip(renderDisplayArea);

            if (rect.Empty)
                return null;

            return rect;
        }

        public Rect ViewToScreen(Rect rect)
        {
            var position = ViewToScreen(rect.Position);
            var size = ViewToScreen(rect.Size);
            rect = new Rect(position, size);

            rect.Clip(renderDisplayArea);

            if (rect.Empty)
                return null;

            return rect;
        }

        public Position ScreenToGame(Position position)
        {
            position = ScreenToView(position);

            return new Position(Misc.Round(position.X / WindowFactorX), Misc.Round(position.Y / WindowFactorY));
        }

        public Position ScreenToView(Position position)
        {
            int relX = position.X - renderDisplayArea.X;
            int relY = position.Y - renderDisplayArea.Y;
            int rotatedX;
            int rotatedY;

            switch (rotation)
            {
                case Rotation.None:
                default:
                    rotatedX = relX;
                    rotatedY = relY;
                    break;
                case Rotation.Deg90:
                    rotatedX = relY;
                    rotatedY = renderDisplayArea.Width - relX;
                    break;
                case Rotation.Deg180:
                    rotatedX = renderDisplayArea.Width - relX;
                    rotatedY = renderDisplayArea.Height - relY;
                    break;
                case Rotation.Deg270:
                    rotatedX = renderDisplayArea.Height - relY;
                    rotatedY = relX;
                    break;
            }

            int x = Misc.Round(sizeFactorX * rotatedX);
            int y = Misc.Round(sizeFactorY * rotatedY);

            return new Position(x, y);
        }

        public Size ScreenToView(Size size)
        {
            bool swapDimensions = rotation == Rotation.Deg90 || rotation == Rotation.Deg270;

            int width = (swapDimensions) ? size.Height : size.Width;
            int height = (swapDimensions) ? size.Width : size.Height;

            return new Size(Misc.Round(sizeFactorX * width), Misc.Round(sizeFactorY * height));
        }

        public Rect ScreenToView(Rect rect)
        {
            var clippedRect = new Rect(rect);

            clippedRect.Clip(renderDisplayArea);

            if (clippedRect.Empty)
                return null;

            var position = ScreenToView(clippedRect.Position);
            var size = ScreenToView(clippedRect.Size);

            return new Rect(position, size);
        }

        public void Dispose()
        {
            Dispose(true);
        }

        void Dispose(bool disposing)
        {
            if (!disposed)
            {
                if (disposing)
                {
                    foreach (var layer in layers.Values)
                        layer?.Dispose();

                    layers.Clear();

                    disposed = true;
                }
            }
        }

        public PaletteReplacement PaletteReplacement
        {
            get => paletteReplacement;
            set
            {
                if (paletteReplacement != value)
                {
                    paletteReplacement = value;

                    (GetLayer(Layer.Billboards3D) as RenderLayer).RenderBuffer.Billboard3DShader.SetPaletteReplacement(paletteReplacement);
                    (GetLayer(Layer.Map3D) as RenderLayer).RenderBuffer.Texture3DShader.SetPaletteReplacement(paletteReplacement);
                    (GetLayer(Layer.Map3DBackground) as RenderLayer).RenderBuffer.SkyShader.SetPaletteReplacement(paletteReplacement);
                }
            }
        }

        public int? DrugColorComponent { get; set; } = null;

        public void SetLight(float light)
        {
            (GetLayer(Layer.Billboards3D) as RenderLayer).RenderBuffer.Billboard3DShader.SetLight(light);
            (GetLayer(Layer.Map3D) as RenderLayer).RenderBuffer.Texture3DShader.SetLight(light);
            (GetLayer(Layer.Map3DBackground) as RenderLayer).RenderBuffer.SkyShader.SetLight(light);
        }

        public void SetSkyColorReplacement(uint? skyColor, Color replaceColor)
        {
            (GetLayer(Layer.Billboards3D) as RenderLayer).RenderBuffer.Billboard3DShader.SetSkyColorReplacement(skyColor, replaceColor);
            (GetLayer(Layer.Map3D) as RenderLayer).RenderBuffer.Texture3DShader.SetSkyColorReplacement(skyColor, replaceColor);
        }
    }
}
