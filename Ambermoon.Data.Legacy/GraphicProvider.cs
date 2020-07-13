﻿using System;
using System.Collections.Generic;
using System.Linq;

namespace Ambermoon.Data.Legacy
{
    public class GraphicProvider : IGraphicProvider
    {
        struct GraphicFile
        {
            public string File;
            public int[] SubFiles; // null means all
            public int FileIndexOffset;

            public GraphicFile(string file, int fileIndexOffset = 0)
            {
                File = file;
                SubFiles = null;
                FileIndexOffset = fileIndexOffset;
            }

            public GraphicFile(string file, int fileIndexOffset, params int[] subFiles)
            {
                File = file;
                SubFiles = subFiles;
                FileIndexOffset = fileIndexOffset;
            }
        };

        readonly GameData gameData;
        public Dictionary<int, Graphic> Palettes { get; }

        public GraphicProvider(GameData gameData)
        {
            this.gameData = gameData;
            var graphicReader = new GraphicReader();
            Palettes = gameData.Files[paletteFile].Files.ToDictionary(f => f.Key, f => ReadPalette(graphicReader, f.Value));

            foreach (GraphicType type in Enum.GetValues(typeof(GraphicType)))
            {
                LoadGraphics(type);
            }
        }

        Graphic ReadPalette(GraphicReader graphicReader, IDataReader reader)
        {
            reader.Position = 0;
            var paletteGraphic = new Graphic();
            graphicReader.ReadGraphic(paletteGraphic, reader, paletteGraphicInfo);
            return paletteGraphic;
        }

        static GraphicInfo paletteGraphicInfo = new GraphicInfo
        {
            Width = 32, Height = 1, GraphicFormat = GraphicFormat.XRGB16
        };
        static readonly string paletteFile = "Palettes.amb";
        static readonly Dictionary<GraphicType, GraphicFile[]> graphicFiles = new Dictionary<GraphicType, GraphicFile[]>();
        readonly Dictionary<GraphicType, List<Graphic>> graphics = new Dictionary<GraphicType, List<Graphic>>();

        static void AddGraphicFiles(GraphicType type, params GraphicFile[] files)
        {
            graphicFiles.Add(type, files);
        }

        static GraphicProvider()
        {
            AddGraphicFiles(GraphicType.Tileset1, new GraphicFile("1Icon_gfx.amb", 0, 1));
            AddGraphicFiles(GraphicType.Tileset2, new GraphicFile("3Icon_gfx.amb", 0, 2));
            AddGraphicFiles(GraphicType.Tileset3, new GraphicFile("2Icon_gfx.amb", 0, 3));
            AddGraphicFiles(GraphicType.Tileset4, new GraphicFile("2Icon_gfx.amb", 0, 4));
            AddGraphicFiles(GraphicType.Tileset5, new GraphicFile("2Icon_gfx.amb", 0, 5));
            AddGraphicFiles(GraphicType.Tileset6, new GraphicFile("2Icon_gfx.amb", 0, 6));
            AddGraphicFiles(GraphicType.Tileset7, new GraphicFile("2Icon_gfx.amb", 0, 7));
            AddGraphicFiles(GraphicType.Tileset8, new GraphicFile("3Icon_gfx.amb", 0, 8));
            AddGraphicFiles(GraphicType.Player, new GraphicFile("Party_gfx.amb"));
            AddGraphicFiles(GraphicType.Map3D,
                new GraphicFile("2Wall3D.amb"),
                new GraphicFile("3Wall3D.amb"),
                new GraphicFile("2Overlay3D.amb", 172),
                new GraphicFile("3Overlay3D.amb", 172));
            AddGraphicFiles(GraphicType.Portrait, new GraphicFile("Portraits.amb"));
            AddGraphicFiles(GraphicType.Item, new GraphicFile("Object_icons"));
            AddGraphicFiles(GraphicType.Layout, new GraphicFile("Layouts.amb"));
        }

        public List<Graphic> GetGraphics(GraphicType type)
        {
            return graphics[type];
        }

        void LoadGraphics(GraphicType type)
        {
            if (!graphics.ContainsKey(type))
            {
                graphics.Add(type, new List<Graphic>());
                var reader = new GraphicReader();
                var info = GraphicInfoFromType(type);
                var graphicList = graphics[type];

                void LoadGraphic(IDataReader graphicDataReader)
                {
                    graphicDataReader.Position = 0;
                    int end = graphicDataReader.Size - info.DataSize;
                    while (graphicDataReader.Position <= end)
                    {
                        var graphic = new Graphic();
                        reader.ReadGraphic(graphic, graphicDataReader, info);
                        graphicList.Add(graphic);
                    }
                }

                var allFiles = new SortedDictionary<int, IDataReader>();

                foreach (var graphicFile in graphicFiles[type])
                {
                    var containerFile = gameData.Files[graphicFile.File];

                    if (graphicFile.SubFiles == null)
                    {
                        foreach (var file in containerFile.Files)
                        {
                            // TODO: wall texture containers have the same files multiple times (e.g. 116). This might be on purpose but I'm not sure.
                            allFiles[graphicFile.FileIndexOffset + file.Key] = file.Value;
                        }
                    }
                    else
                    {
                        foreach (var file in graphicFile.SubFiles)
                        {
                            allFiles[graphicFile.FileIndexOffset + file] = containerFile.Files[file];
                        }
                    }
                }

                foreach (var file in allFiles)
                {
                    LoadGraphic(file.Value);
                }
            }
        }

        GraphicInfo GraphicInfoFromType(GraphicType type)
        {
            var info = new GraphicInfo
            {
                Width = 16,
                Height = 16,
                GraphicFormat = GraphicFormat.Palette5Bit
            };

            switch (type)
            {
                case GraphicType.Tileset1:
                case GraphicType.Tileset2:
                    info.Alpha = true;
                    break;
                case GraphicType.Tileset3:
                    info.Alpha = true;
                    break;
                case GraphicType.Tileset4:
                case GraphicType.Tileset5:
                case GraphicType.Tileset6:
                case GraphicType.Tileset7:
                    info.Alpha = true;
                    break;
                case GraphicType.Tileset8:
                    info.Alpha = true;
                    break;
                case GraphicType.Player:
                    info.Width = 16;
                    info.Height = 32;
                    info.Alpha = true;
                    break;
                case GraphicType.Portrait:
                    info.Width = 32;
                    info.Height = 32;
                    break;
                case GraphicType.Item:
                    info.Width = 16;
                    info.Height = 16;
                    break;
                case GraphicType.Layout:
                    info.Width = 320;
                    info.Height = 163;
                    info.GraphicFormat = GraphicFormat.Palette3Bit;
                    info.PaletteOffset = 24;
                    info.Alpha = true;
                    break;
                case GraphicType.Map3D:
                    info.Width = 128;
                    info.Height = 80;
                    info.GraphicFormat = GraphicFormat.Palette4Bit;
                    info.Alpha = true;
                    break;
                // TODO
            }

            return info;
        }
    }
}
