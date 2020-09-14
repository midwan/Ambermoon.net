﻿using Ambermoon.Data;

namespace Ambermoon.Render
{
    internal class Player2D : Character2D, IRenderPlayer
    {
        readonly Player player;
        readonly IMapManager mapManager;

        // TODO: gameData.PlayerAnimationInfo is only for Lyramion. Offsets need to be increased by World * 17 later.
        public Player2D(Game game, IRenderLayer layer, Player player, RenderMap2D map,
            ISpriteFactory spriteFactory, IGameData gameData, Position startPosition,
            IMapManager mapManager)
            : base(game, layer, TextureAtlasManager.Instance.GetOrCreate(Layer.Characters),
                  spriteFactory, gameData.PlayerAnimationInfo, map, startPosition, 7u,
                  gameData.WorldPlayerAnimationInfo)
        {
            this.player = player;
            this.mapManager = mapManager;
        }

        public bool Move(int x, int y, uint ticks, CharacterDirection? prevDirection = null,
            bool updateDirectionIfNotMoving = true) // x,y in tiles
        {
            if (player.MovementAbility == PlayerMovementAbility.NoMovement)
                return false;

            bool canMove = true;
            var map = Map.Map;
            int newX = Position.X + x;
            int newY = Position.Y + y;
            Map.Tile tile = null;

            if (!map.IsWorldMap)
            {
                // Don't leave the map.
                // Note that the player is 2 tiles tall in non-world maps
                // and the position is the upper tile so he is allowed to
                // move up to y = -1 and only down to y = map.Height - 1.
                if (newX < 0 || newY < -1 || newX >= map.Width || newY >= map.Height - 1)
                    canMove = false;
                else
                    tile = Map[(uint)newX, (uint)newY + 1];
            }
            else
            {
                while (newX < 0)
                    newX += map.Width;
                while (newY < 0)
                    newY += map.Height;

                tile = Map[(uint)newX, (uint)newY];
            }

            if (canMove)
            {
                switch (tile.Type)
                {
                    case Data.Map.TileType.Free:
                    case Data.Map.TileType.ChairUp:
                    case Data.Map.TileType.ChairRight:
                    case Data.Map.TileType.ChairDown:
                    case Data.Map.TileType.ChairLeft:
                    case Data.Map.TileType.Bed:
                    case Data.Map.TileType.Invisible:
                        canMove = true; // no movement was checked above
                        break;
                    case Data.Map.TileType.Obstacle:
                        canMove = player.MovementAbility >= PlayerMovementAbility.WitchBroom;
                        break;
                    case Data.Map.TileType.Water:
                        canMove = player.MovementAbility >= PlayerMovementAbility.Swimming;
                        break;
                    case Data.Map.TileType.Ocean:
                        canMove = player.MovementAbility >= PlayerMovementAbility.Sailing;
                        break;
                    case Data.Map.TileType.Mountain:
                        canMove = player.MovementAbility >= PlayerMovementAbility.Eagle;
                        break;
                    default:
                        canMove = false;
                        break;
                }
            }

            // TODO: display OUCH if moving against obstacles

            if (canMove)
            {
                var oldMap = map;
                int scrollX = 0;
                int scrollY = 0;
                prevDirection ??= Direction;
                var newDirection = CharacterDirection.Down;

                if (x > 0 && (map.IsWorldMap || (newX >= 6 && newX <= map.Width - 6)))
                    scrollX = 1;
                else if (x < 0 && (map.IsWorldMap || (newX <= map.Width - 7 && newX >= 5)))
                    scrollX = -1;

                if (y > 0 && (map.IsWorldMap || (newY >= 4 && newY <= map.Height - 5)))
                    scrollY = 1;
                else if (y < 0 && (map.IsWorldMap || (newY <= map.Height - 7 && newY >= 3)))
                    scrollY = -1;

                if (y > 0)
                    newDirection = CharacterDirection.Down;
                else if (y < 0)
                    newDirection = CharacterDirection.Up;
                else if (x > 0)
                    newDirection = CharacterDirection.Right;
                else if (x < 0)
                    newDirection = CharacterDirection.Left;

                Map.Scroll(scrollX, scrollY);

                if (oldMap == Map.Map)
                {
                    bool frameReset = NumFrames == 1 || newDirection != prevDirection;
                    var prevState = CurrentState;

                    MoveTo(oldMap, (uint)newX, (uint)newY, ticks, frameReset, null);
                    // We trigger with our lower half so add 1 to y in non-world maps.
                    Map.TriggerEvents(this, MapEventTrigger.Move, (uint)newX,
                        (uint)newY + (oldMap.IsWorldMap ? 0u : 1u), mapManager, ticks);

                    if (oldMap == Map.Map) // might have changed by map change events
                    {
                        if (!frameReset && CurrentState == prevState)
                            SetCurrentFrame((CurrentFrame + 1) % NumFrames);

                        player.Position.X = Position.X - (int)Map.ScrollX;
                        player.Position.Y = Position.Y - (int)Map.ScrollY;

                        Visible = tile.Type != Data.Map.TileType.Invisible;
                    }
                }
                else
                {
                    // adjust player position on map transition
                    var position = Map.GetCenterPosition();

                    MoveTo(Map.Map, (uint)position.X, (uint)position.Y, ticks, false, Direction);
                    Map.TriggerEvents(this, MapEventTrigger.Move, (uint)position.X,
                        (uint)position.Y + (Map.Map.IsWorldMap ? 0u : 1u), mapManager, ticks);

                    if (Map.Map.Type == MapType.Map2D)
                    {
                        player.Position.X = Position.X - (int)Map.ScrollX;
                        player.Position.Y = Position.Y - (int)Map.ScrollY;

                        // Note: For 3D maps the game/3D map will handle player position updating.
                    }
                }
            }
            else if (updateDirectionIfNotMoving)
            {
                // If not able to move, the direction should be adjusted
                var newDirection = Direction;

                if (y > 0)
                    newDirection = CharacterDirection.Down;
                else if (y < 0)
                    newDirection = CharacterDirection.Up;
                else if (x > 0)
                    newDirection = CharacterDirection.Right;
                else if (x < 0)
                    newDirection = CharacterDirection.Left;

                if (newDirection != Direction)
                    MoveTo(Map.Map, (uint)Position.X, (uint)Position.Y, ticks, true, newDirection);
            }

            return canMove;
        }

        public override void MoveTo(Map map, uint x, uint y, uint ticks, bool frameReset, CharacterDirection? newDirection)
        {
            if (Map.Map != map)
            {
                Visible = true; // reset visibility before changing map

                Padding.Y = map.IsWorldMap ? -4 : 0;
            }

            base.MoveTo(map, x, y, ticks, frameReset, newDirection);

            player.Position.X = Position.X;
            player.Position.Y = Position.Y;
        }

        public override void Update(uint ticks)
        {
            // do not animate so don't call base.Update here
        }
    }
}
