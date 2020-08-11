﻿using Ambermoon.Data;
using Ambermoon.Render;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Ambermoon
{
    public class Game
    {
        class NameProvider : ITextNameProvider
        {
            readonly Game game;

            public NameProvider(Game game)
            {
                this.game = game;
            }

            /// <inheritdoc />
            public string LeadName => game.party.First(p => p.Alive).Name;
            /// <inheritdoc />
            public string SelfName => game.CurrentPartyMember.Name;
            /// <inheritdoc />
            public string CastName => game.CurrentCaster?.Name;
            /// <inheritdoc />
            public string InvnName => game.CurrentInventory?.Name;
            /// <inheritdoc />
            public string SubjName => game.CurrentPartyMember?.Name; // TODO
            /// <inheritdoc />
            public string Sex1Name => game.CurrentPartyMember.Gender == Gender.Male ? "he" : "she"; // TODO
            /// <inheritdoc />
            public string Sex2Name => game.CurrentPartyMember.Gender == Gender.Male ? "his" : "her"; // TODO
        }

        const uint TicksPerSecond = 60; // TODO
        uint currentTicks = 0;
        uint lastMapTicksReset = 0;
        uint lastKeyTicksReset = 0;
        bool ingame = false;
        readonly NameProvider nameProvider;
        readonly UI.Layout layout;
        readonly IMapManager mapManager;
        readonly IItemManager itemManager;
        readonly IRenderView renderView;
        Player player;
        readonly PartyMember[] party = new PartyMember[6];
        PartyMember CurrentPartyMember { get; } = null;
        PartyMember CurrentInventory { get; } = null;
        PartyMember CurrentCaster { get; } = null;
        bool is3D = false;
        readonly bool[] keys = new bool[Enum.GetValues(typeof(Key)).Length];
        /// <summary>
        /// All words you have heard about in conversations.
        /// </summary>
        readonly List<string> dictionary = new List<string>();

        // Rendering
        RenderMap2D renderMap2D = null;
        Player2D player2D = null;
        RenderMap3D renderMap3D = null;
        Player3D player3D = null;
        readonly ICamera3D camera3D = null;
        readonly IRenderText messageText = null;

        public Game(IRenderView renderView, IMapManager mapManager, IItemManager itemManager)
        {
            nameProvider = new NameProvider(this);
            this.renderView = renderView;
            this.mapManager = mapManager;
            this.itemManager = itemManager;
            camera3D = renderView.Camera3D;
            messageText = renderView.RenderTextFactory.Create();
            messageText.Layer = renderView.GetLayer(Layer.Text);
            layout = new UI.Layout(renderView);

            // TODO: values should come from the character select menu
            party[0] = PartyMember.Create("Thalion", 2, Gender.Male);
            CurrentPartyMember = party[0];
        }

        public void Update(double deltaTime)
        {
            uint add = (uint)Util.Round(TicksPerSecond * (float)deltaTime);

            if (currentTicks <= uint.MaxValue - add)
                currentTicks += add;
            else
                currentTicks = (uint)(((long)currentTicks + add) % uint.MaxValue);

            if (ingame)
            {
                // TODO ingame rendering

                if (is3D)
                {
                    // TODO
                }
                else // 2D
                {
                    var animationTicks = currentTicks >= lastMapTicksReset ? currentTicks - lastMapTicksReset : (uint)((long)currentTicks + uint.MaxValue - lastMapTicksReset);
                    renderMap2D.UpdateAnimations(animationTicks);
                }
            }

            var keyTicks = currentTicks >= lastKeyTicksReset ? currentTicks - lastKeyTicksReset : (uint)((long)currentTicks + uint.MaxValue - lastKeyTicksReset);

            if (keyTicks >= TicksPerSecond / 8)
            {
                Move();

                lastKeyTicksReset = currentTicks;
            }
        }

        void ResetMoveKeys()
        {
            keys[(int)Key.Up] = false;
            keys[(int)Key.Down] = false;
            keys[(int)Key.Left] = false;
            keys[(int)Key.Right] = false;
            lastKeyTicksReset = currentTicks;
        }

        internal void Start2D(Map map, uint playerX, uint playerY, CharacterDirection direction)
        {
            if (map.Type != MapType.Map2D)
                throw new AmbermoonException(ExceptionScope.Application, "Given map is not 2D.");

            ResetMoveKeys();

            if (renderMap2D.Map != map)
            {
                renderMap2D.SetMap
                (
                    map,
                    (uint)Util.Limit(0, (int)playerX - RenderMap2D.NUM_VISIBLE_TILES_X / 2, map.Width - RenderMap2D.NUM_VISIBLE_TILES_X),
                    (uint)Util.Limit(0, (int)playerY - RenderMap2D.NUM_VISIBLE_TILES_Y / 2, map.Height - RenderMap2D.NUM_VISIBLE_TILES_Y)
                );
            }
            else
            {
                renderMap2D.ScrollTo
                (
                    (uint)Util.Limit(0, (int)playerX - RenderMap2D.NUM_VISIBLE_TILES_X / 2, map.Width - RenderMap2D.NUM_VISIBLE_TILES_X),
                    (uint)Util.Limit(0, (int)playerY - RenderMap2D.NUM_VISIBLE_TILES_Y / 2, map.Height - RenderMap2D.NUM_VISIBLE_TILES_Y),
                    true
                );
            }

            player2D.Visible = true;
            player2D.MoveTo(map, playerX, playerY, currentTicks, true, direction);

            var mapOffset = map.MapOffset;
            player.Position.X = mapOffset.X + (int)playerX - (int)renderMap2D.ScrollX;
            player.Position.Y = mapOffset.Y + (int)playerY - (int)renderMap2D.ScrollY;
            player.Direction = direction;

            renderMap3D.Destroy();

            is3D = false;
            renderView.GetLayer(Layer.Map3D).Visible = false;
            renderView.GetLayer(Layer.Billboards3D).Visible = false;
            for (int i = (int)Global.First2DLayer; i <= (int)Global.Last2DLayer; ++i)
                renderView.GetLayer((Layer)i).Visible = true;
        }

        internal void Start3D(Map map, uint playerX, uint playerY, CharacterDirection direction)
        {
            if (map.Type != MapType.Map3D)
                throw new AmbermoonException(ExceptionScope.Application, "Given map is not 3D.");

            ResetMoveKeys();

            renderMap3D = new RenderMap3D(map, mapManager, renderView, playerX, playerY, direction);
            renderMap2D.Destroy();
            player3D = new Player3D(this, mapManager, camera3D, renderMap3D, 0, 0);
            player3D.SetPosition((int)playerX, (int)playerY, currentTicks);
            player2D.Visible = false;
            player.Position.X = (int)playerX;
            player.Position.Y = (int)playerY;
            player.Direction = direction;

            is3D = true;
            renderView.GetLayer(Layer.Map3D).Visible = true;
            renderView.GetLayer(Layer.Billboards3D).Visible = true;
            for (int i = (int)Global.First2DLayer; i <= (int)Global.Last2DLayer; ++i)
                renderView.GetLayer((Layer)i).Visible = false;
        }

        public void StartNew()
        {
            ingame = true;
            layout.SetLayout(UI.LayoutType.Map);
            player = new Player();
            var map = mapManager.GetMap(258u); // grandfather's house
            renderMap2D = new RenderMap2D(map, mapManager, renderView);
            renderMap3D = new RenderMap3D(null, mapManager, renderView, 0, 0, CharacterDirection.Up);            
            player2D = new Player2D(this, renderView.GetLayer(Layer.Characters), player, renderMap2D,
                renderView.SpriteFactory, renderView.GameData, new Position(2, 2), mapManager);
            player2D.Visible = true;
            player3D = new Player3D(this, mapManager, camera3D, renderMap3D, 0, 0);
            player.MovementAbility = PlayerMovementAbility.Walking;
            // TODO
        }

        public void LoadGame()
        {
            // TODO
        }

        public void Continue()
        {
            // TODO: load latest game
        }

        public void ShowMessage(Rect bounds, string text, TextColor color, bool shadow, TextAlign textAlign = TextAlign.Left)
        {
            messageText.Text = renderView.TextProcessor.ProcessText(text, nameProvider, dictionary);
            messageText.TextColor = color;
            messageText.Shadow = shadow;
            messageText.Place(bounds, textAlign);
            messageText.Visible = true;
        }

        public void HideMessage()
        {
            messageText.Visible = false;
        }

        void Move()
        {
            if (keys[(int)Key.Left] && !keys[(int)Key.Right])
            {
                if (!is3D)
                {
                    // diagonal movement is handled in up/down
                    if (!keys[(int)Key.Up] && !keys[(int)Key.Down])
                        player2D.Move(-1, 0, currentTicks);
                }
                else
                    player3D.TurnLeft(15.0f); // TODO
            }
            if (keys[(int)Key.Right] && !keys[(int)Key.Left])
            {
                if (!is3D)
                {
                    // diagonal movement is handled in up/down
                    if (!keys[(int)Key.Up] && !keys[(int)Key.Down])
                        player2D.Move(1, 0, currentTicks);
                }
                else
                    player3D.TurnRight(15.0f); // TODO
            }
            if (keys[(int)Key.Up] && !keys[(int)Key.Down])
            {
                if (!is3D)
                {
                    var prevDirection = player2D.Direction;
                    int x = keys[(int)Key.Left] && !keys[(int)Key.Right] ? -1 :
                        keys[(int)Key.Right] && !keys[(int)Key.Left] ? 1 : 0;

                    if (!player2D.Move(x, -1, currentTicks) && x != 0)
                    {
                        if (!player2D.Move(0, -1, currentTicks, prevDirection))
                            player2D.Move(x, 0, currentTicks, prevDirection);
                    }
                }
                else
                    player3D.MoveForward(0.75f, currentTicks); // TODO
            }
            if (keys[(int)Key.Down] && !keys[(int)Key.Up])
            {
                if (!is3D)
                {
                    var prevDirection = player2D.Direction;
                    int x = keys[(int)Key.Left] && !keys[(int)Key.Right] ? -1 :
                        keys[(int)Key.Right] && !keys[(int)Key.Left] ? 1 : 0;

                    if (!player2D.Move(x, 1, currentTicks, null, false) && x != 0)
                    {
                        if (!player2D.Move(0, 1, currentTicks, prevDirection, false))
                            player2D.Move(x, 0, currentTicks, prevDirection);
                    }
                }
                else
                    player3D.MoveBackward(0.75f, currentTicks); // TODO
            }
        }

        public void OnKeyDown(Key key, KeyModifiers modifiers)
        {
            keys[(int)key] = true;

            Move();

            lastKeyTicksReset = currentTicks;
        }

        public void OnKeyUp(Key key, KeyModifiers modifiers)
        {
            keys[(int)key] = false;
        }

        public void OnKeyChar(char keyChar)
        {

        }

        public void OnMouseDown(MouseButtons buttons)
        {

        }
    }
}
