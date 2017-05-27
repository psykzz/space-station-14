﻿using Lidgren.Network;
using SFML.Graphics;
using SFML.System;
using SFML.Window;
using SS14.Client.GameObjects;
using SS14.Client.Graphics;
using SS14.Client.Graphics.Event;
using SS14.Client.Graphics.Render;
using SS14.Client.Graphics.Shader;
using SS14.Client.Graphics.Sprite;
using SS14.Client.Interfaces.GameTimer;
using SS14.Client.Interfaces.GOC;
using SS14.Client.Interfaces.Lighting;
using SS14.Client.Interfaces.Map;
using SS14.Client.Interfaces.Player;
using SS14.Client.Interfaces.Resource;
using SS14.Client.Interfaces.Serialization;
using SS14.Client.Interfaces.State;
using SS14.Client.Helpers;
using SS14.Client.Lighting;
using SS14.Client.UserInterface.Components;
using SS14.Client.UserInterface.Inventory;
using SS14.Shared;
using SS14.Shared.GameObjects;
using SS14.Shared.GameStates;
using SS14.Shared.IoC;
using SS14.Shared.Maths;
using System;
using System.Collections.Generic;
using System.Linq;
using EntityManager = SS14.Client.GameObjects.EntityManager;
using KeyEventArgs = SFML.Window.KeyEventArgs;
using SS14.Shared.Log;

namespace SS14.Client.State.States
{
    public class GameScreen : State, IState
    {
        #region Variables
        public DateTime LastUpdate;
        public DateTime Now;

        public int ScreenHeightTiles = 12;
        public int ScreenWidthTiles = 15; // How many tiles around us do we draw?
        public string SpawnType;

        private float _realScreenHeightTiles;
        private float _realScreenWidthTiles;

        private bool _recalculateScene = true;
        private bool _redrawOverlay = true;
        private bool _redrawTiles = true;
        private bool _showDebug; // show AABBs & Bounding Circles on Entities.

        private RenderImage _baseTarget;
        private Sprite _baseTargetSprite;

        private EntityManager _entityManager;

        private GaussianBlur _gaussianBlur;

        private List<RenderImage> _cleanupList = new List<RenderImage>();
        private List<Sprite> _cleanupSpriteList = new List<Sprite>();

        private SpriteBatch _wallBatch;
        private SpriteBatch _wallTopsBatch;
        private SpriteBatch _floorBatch;
        private SpriteBatch _gasBatch;
        private SpriteBatch _decalBatch;

        #region gameState stuff
        private readonly Dictionary<uint, GameState> _lastStates = new Dictionary<uint, GameState>();
        private uint _currentStateSequence; //We only ever want a newer state than the current one
        #endregion

        #region Mouse/Camera stuff
        public Vector2i MousePosScreen = new Vector2i();
        public Vector2f MousePosWorld = new Vector2f();
        #endregion

        #region UI Variables
        private int _prevScreenWidth = 0;
        private int _prevScreenHeight = 0;

        private MenuWindow _menu;
        private Chatbox _gameChat;
        private HandsGui _handsGui;
        private HumanComboGui _combo;
        private HealthPanel _healthPanel;
        private ImageButton _inventoryButton;
        private ImageButton _statusButton;
        private ImageButton _menuButton;
        #endregion

        #region Lighting
        private Sprite _lightTargetIntermediateSprite;
        private Sprite _lightTargetSprite;

        private bool bPlayerVision = true;
        private bool bFullVision = false;
        private bool debugWallOccluders = false;
        private bool debugPlayerShadowMap = false;
        private bool debugHitboxes = false;
        public bool BlendLightMap = true;

        private TechniqueList LightblendTechnique;
        private GLSLShader Lightmap;

        private LightArea lightArea1024;
        private LightArea lightArea128;
        private LightArea lightArea256;
        private LightArea lightArea512;

        private ILight playerVision;
        private ISS14Serializer serializer;

        private RenderImage playerOcclusionTarget;
        private RenderImage _occluderDebugTarget;
        private RenderImage _lightTarget;
        private RenderImage _lightTargetIntermediate;
        private RenderImage _composedSceneTarget;
        private RenderImage _overlayTarget;
        private RenderImage _sceneTarget;
        private RenderImage _tilesTarget;
        private RenderImage screenShadows;
        private RenderImage shadowBlendIntermediate;
        private RenderImage shadowIntermediate;

        //private QuadRenderer quadRenderer;
        private ShadowMapResolver shadowMapResolver;

        #endregion

        #endregion

        public GameScreen(IDictionary<Type, object> managers) : base(managers)
        {

        }

        #region IState Members

        public void Startup()
        {
            LastUpdate = DateTime.Now;
            Now = DateTime.Now;

            _cleanupList = new List<RenderImage>();
            _cleanupSpriteList = new List<Sprite>();

            UserInterfaceManager.DisposeAllComponents();

            //Init serializer
            serializer = IoCManager.Resolve<ISS14Serializer>();

            _entityManager = new EntityManager(NetworkManager);
            IoCManager.Resolve<IEntityManagerContainer>().EntityManager = _entityManager;
            IoCManager.Resolve<IMapManager>().TileChanged += OnTileChanged;
            IoCManager.Resolve<IPlayerManager>().OnPlayerMove += OnPlayerMove;

            NetworkManager.MessageArrived += NetworkManagerMessageArrived;
            NetworkManager.RequestMap();
            // TODO This should go somewhere else, there should be explicit session setup and teardown at some point.
            NetworkManager.SendClientName(ConfigurationManager.GetPlayerName());

            // Create new
            _gaussianBlur = new GaussianBlur(ResourceManager);

            _realScreenWidthTiles = (float)CluwneLib.Screen.Size.X / MapManager.TileSize;
            _realScreenHeightTiles = (float)CluwneLib.Screen.Size.Y / MapManager.TileSize;

            InitializeRenderTargets();
            InitializeSpriteBatches();
            InitalizeLighting();
            InitializeGUI();

        }
       
        private void InitializeRenderTargets()
        {
            _baseTarget = new RenderImage("baseTarget", CluwneLib.Screen.Size.X, CluwneLib.Screen.Size.Y, true);
            _cleanupList.Add(_baseTarget);

            _baseTargetSprite = new Sprite(_baseTarget.Texture);
            _cleanupSpriteList.Add(_baseTargetSprite);

            _sceneTarget = new RenderImage("sceneTarget", CluwneLib.Screen.Size.X, CluwneLib.Screen.Size.Y, true);
            _cleanupList.Add(_sceneTarget);
            _tilesTarget = new RenderImage("tilesTarget", CluwneLib.Screen.Size.X, CluwneLib.Screen.Size.Y, true);
            _cleanupList.Add(_tilesTarget);

            _overlayTarget = new RenderImage("OverlayTarget", CluwneLib.Screen.Size.X, CluwneLib.Screen.Size.Y, true);
            _cleanupList.Add(_overlayTarget);

            //_overlayTarget.SourceBlend = AlphaBlendOperation.SourceAlpha;
            //_overlayTarget.DestinationBlend = AlphaBlendOperation.InverseSourceAlpha;
            //_overlayTarget.SourceBlendAlpha = AlphaBlendOperation.SourceAlpha;
            //_overlayTarget.DestinationBlendAlpha = AlphaBlendOperation.InverseSourceAlpha;

            _overlayTarget.BlendSettings.ColorSrcFactor = BlendMode.Factor.SrcAlpha;
            _overlayTarget.BlendSettings.ColorDstFactor = BlendMode.Factor.OneMinusSrcAlpha;
            _overlayTarget.BlendSettings.AlphaSrcFactor = BlendMode.Factor.SrcAlpha;
            _overlayTarget.BlendSettings.AlphaDstFactor = BlendMode.Factor.OneMinusSrcAlpha;


            _composedSceneTarget = new RenderImage("composedSceneTarget", CluwneLib.Screen.Size.X, CluwneLib.Screen.Size.Y,
                                                 ImageBufferFormats.BufferRGB888A8);
            _cleanupList.Add(_composedSceneTarget);

            _lightTarget = new RenderImage("lightTarget", CluwneLib.Screen.Size.X, CluwneLib.Screen.Size.Y, ImageBufferFormats.BufferRGB888A8);

            _cleanupList.Add(_lightTarget);
            _lightTargetSprite = new Sprite(_lightTarget.Texture);

            _cleanupSpriteList.Add(_lightTargetSprite);

            _lightTargetIntermediate = new RenderImage("lightTargetIntermediate", CluwneLib.Screen.Size.X, CluwneLib.Screen.Size.Y,
                                                      ImageBufferFormats.BufferRGB888A8);
            _cleanupList.Add(_lightTargetIntermediate);
            _lightTargetIntermediateSprite = new Sprite(_lightTargetIntermediate.Texture);
            _cleanupSpriteList.Add(_lightTargetIntermediateSprite);
        }

        private void InitializeSpriteBatches()
        {
            _gasBatch = new SpriteBatch();
            //_gasBatch.SourceBlend                   = AlphaBlendOperation.SourceAlpha;
            //_gasBatch.DestinationBlend              = AlphaBlendOperation.InverseSourceAlpha;
            //_gasBatch.SourceBlendAlpha              = AlphaBlendOperation.SourceAlpha;
            //_gasBatch.DestinationBlendAlpha         = AlphaBlendOperation.InverseSourceAlpha;
            _gasBatch.BlendingSettings.ColorSrcFactor = BlendMode.Factor.SrcAlpha;
            _gasBatch.BlendingSettings.ColorDstFactor = BlendMode.Factor.OneMinusDstAlpha;
            _gasBatch.BlendingSettings.AlphaSrcFactor = BlendMode.Factor.SrcAlpha;
            _gasBatch.BlendingSettings.AlphaDstFactor = BlendMode.Factor.OneMinusSrcAlpha;

            _wallTopsBatch = new SpriteBatch();
            //_wallTopsBatch.SourceBlend                   = AlphaBlendOperation.SourceAlpha;
            //_wallTopsBatch.DestinationBlend              = AlphaBlendOperation.InverseSourceAlpha;
            //_wallTopsBatch.SourceBlendAlpha              = AlphaBlendOperation.SourceAlpha;
            //_wallTopsBatch.DestinationBlendAlpha         = AlphaBlendOperation.InverseSourceAlpha;
            _wallTopsBatch.BlendingSettings.ColorSrcFactor = BlendMode.Factor.SrcAlpha;
            _wallTopsBatch.BlendingSettings.ColorDstFactor = BlendMode.Factor.OneMinusDstAlpha;
            _wallTopsBatch.BlendingSettings.AlphaSrcFactor = BlendMode.Factor.SrcAlpha;
            _wallTopsBatch.BlendingSettings.AlphaDstFactor = BlendMode.Factor.OneMinusSrcAlpha;


            _decalBatch = new SpriteBatch();
            //_decalBatch.SourceBlend                   = AlphaBlendOperation.SourceAlpha;
            //_decalBatch.DestinationBlend              = AlphaBlendOperation.InverseSourceAlpha;
            //_decalBatch.SourceBlendAlpha              = AlphaBlendOperation.SourceAlpha;
            //_decalBatch.DestinationBlendAlpha         = AlphaBlendOperation.InverseSourceAlpha;
            _decalBatch.BlendingSettings.ColorSrcFactor = BlendMode.Factor.SrcAlpha;
            _decalBatch.BlendingSettings.ColorDstFactor = BlendMode.Factor.OneMinusDstAlpha;
            _decalBatch.BlendingSettings.AlphaSrcFactor = BlendMode.Factor.SrcAlpha;
            _decalBatch.BlendingSettings.AlphaDstFactor = BlendMode.Factor.OneMinusSrcAlpha;


            _floorBatch = new SpriteBatch();
            _wallBatch = new SpriteBatch();
        }

        private Vector2i _gameChatSize = new Vector2i(475, 175); // TODO: Move this magic variable

        private void UpdateGUIPosition()
        {
            _gameChat.Position = new Vector2i((int)CluwneLib.Screen.Size.X - _gameChatSize.X - 10, 10);

            int hotbar_pos_y = (int)CluwneLib.Screen.Size.Y - 88;

            _handsGui.Position = new Vector2i(5, hotbar_pos_y + 7);

            // 712 is width of hotbar background I think?
            _combo.Position = new Vector2i(712 - _combo.ClientArea.Width + 5,
                                       hotbar_pos_y - _combo.ClientArea.Height - 5);

            _healthPanel.Position = new Vector2i(712 - 1, hotbar_pos_y + 11);

            _inventoryButton.Position = new Vector2i(172, hotbar_pos_y + 2);
            _statusButton.Position = new Vector2i(_inventoryButton.ClientArea.Right(), _inventoryButton.Position.Y);

            _menuButton.Position = new Vector2i(_statusButton.ClientArea.Right(), _statusButton.Position.Y);

    }

        private void InitializeGUI()
        {
            // Setup the ESC Menu
            _menu = new MenuWindow();
            UserInterfaceManager.AddComponent(_menu);
            _menu.SetVisible(false);

            //Init GUI components
            _gameChat = new Chatbox("gamechat", _gameChatSize, ResourceManager);
            _gameChat.TextSubmitted += ChatTextboxTextSubmitted;
            UserInterfaceManager.AddComponent(_gameChat);

            //UserInterfaceManager.AddComponent(new StatPanelComponent(ConfigurationManager.GetPlayerName(), PlayerManager, NetworkManager, ResourceManager));

            int hotbar_pos_y = (int)CluwneLib.Screen.Size.Y - 88;

            _handsGui = new HandsGui();
            _handsGui.Position = new Vector2i(5, hotbar_pos_y + 7);
            UserInterfaceManager.AddComponent(_handsGui);

            _combo = new HumanComboGui(PlayerManager, NetworkManager, ResourceManager, UserInterfaceManager);
            _combo.Position = new Vector2i(712 - _combo.ClientArea.Width + 5,
                                       hotbar_pos_y - _combo.ClientArea.Height - 5);
            _combo.Update(0);
            UserInterfaceManager.AddComponent(_combo);

            _healthPanel = new HealthPanel();
            _healthPanel.Position = new Vector2i(711, hotbar_pos_y + 11);
            _healthPanel.Update(0);
            UserInterfaceManager.AddComponent(_healthPanel);

            _inventoryButton = new ImageButton
            {
                ImageNormal = "button_inv",
                Position = new Vector2i(172, hotbar_pos_y + 2)
            };
            _inventoryButton.Update(0);
            _inventoryButton.Clicked += inventoryButton_Clicked;
            UserInterfaceManager.AddComponent(_inventoryButton);

            _statusButton = new ImageButton
            {
                ImageNormal = "button_status",
                Position =
                    new Vector2i(_inventoryButton.ClientArea.Right(), _inventoryButton.Position.Y)
            };
            _statusButton.Update(0);
            _statusButton.Clicked += statusButton_Clicked;
            UserInterfaceManager.AddComponent(_statusButton);


            _menuButton = new ImageButton
            {
                ImageNormal = "button_menu",
                Position = new Vector2i(_statusButton.ClientArea.Right(), _statusButton.Position.Y)
            };
            _menuButton.Update(0);
            _menuButton.Clicked += menuButton_Clicked;
            UserInterfaceManager.AddComponent(_menuButton);
        }

        private void InitalizeLighting()
        {
            shadowMapResolver = new ShadowMapResolver(ShadowmapSize.Size1024, ShadowmapSize.Size1024,
                                                      ResourceManager);
            shadowMapResolver.LoadContent();
            lightArea128 = new LightArea(ShadowmapSize.Size128);
            lightArea256 = new LightArea(ShadowmapSize.Size256);
            lightArea512 = new LightArea(ShadowmapSize.Size512);
            lightArea1024 = new LightArea(ShadowmapSize.Size1024);

            screenShadows = new RenderImage("screenShadows", CluwneLib.Screen.Size.X, CluwneLib.Screen.Size.Y, ImageBufferFormats.BufferRGB888A8);

            _cleanupList.Add(screenShadows);
            screenShadows.UseDepthBuffer = false;
            shadowIntermediate = new RenderImage("shadowIntermediate", CluwneLib.Screen.Size.X, CluwneLib.Screen.Size.Y,
                                                 ImageBufferFormats.BufferRGB888A8);
            _cleanupList.Add(shadowIntermediate);
            shadowIntermediate.UseDepthBuffer = false;
            shadowBlendIntermediate = new RenderImage("shadowBlendIntermediate", CluwneLib.Screen.Size.X, CluwneLib.Screen.Size.Y,
                                                      ImageBufferFormats.BufferRGB888A8);
            _cleanupList.Add(shadowBlendIntermediate);
            shadowBlendIntermediate.UseDepthBuffer = false;
            playerOcclusionTarget = new RenderImage("playerOcclusionTarget", CluwneLib.Screen.Size.X, CluwneLib.Screen.Size.Y,
                                                    ImageBufferFormats.BufferRGB888A8);
            _cleanupList.Add(playerOcclusionTarget);
            playerOcclusionTarget.UseDepthBuffer = false;

            LightblendTechnique = IoCManager.Resolve<IResourceManager>().GetTechnique("lightblend");
            Lightmap = IoCManager.Resolve<IResourceManager>().GetShader("lightmap");

            playerVision = IoCManager.Resolve<ILightManager>().CreateLight();
            playerVision.SetColor(Color.Blue);
            playerVision.SetRadius(1024);
            playerVision.Move(new Vector2f());


            _occluderDebugTarget = new RenderImage("debug", CluwneLib.Screen.Size.X, CluwneLib.Screen.Size.Y);

        }

        public void Update(FrameEventArgs e)
        {
            LastUpdate = Now;
            Now = DateTime.Now;

            if (CluwneLib.Screen.Size.X != _prevScreenWidth || CluwneLib.Screen.Size.Y != _prevScreenHeight)
            {
                _prevScreenHeight = (int)CluwneLib.Screen.Size.Y;
                _prevScreenWidth = (int)CluwneLib.Screen.Size.X;
                UpdateGUIPosition();
            }

            CluwneLib.TileSize = MapManager.TileSize;

            IoCManager.Resolve<IGameTimer>().UpdateTime(e.FrameDeltaTime);
            _entityManager.ComponentManager.Update(e.FrameDeltaTime);
            _entityManager.Update(e.FrameDeltaTime);
            PlacementManager.Update(MousePosScreen, MapManager);
            PlayerManager.Update(e.FrameDeltaTime);

            if (PlayerManager.ControlledEntity != null)
            {
                CluwneLib.WorldCenter = PlayerManager.ControlledEntity.GetComponent<TransformComponent>(ComponentFamily.Transform).Position;
                MousePosWorld = CluwneLib.ScreenToWorld(MousePosScreen); // Use WorldCenter to calculate, so we need to update again
            }
        }
        public ILight[] currentLightsCache;
        public void Render(FrameEventArgs e)
        {
            CluwneLib.Screen.Clear(Color.Black);
            CluwneLib.TileSize = MapManager.TileSize;

            CalculateAllLights();

            if (PlayerManager.ControlledEntity != null)
            {
                CluwneLib.ScreenViewportSize = new Vector2u(CluwneLib.Screen.Size.X, CluwneLib.Screen.Size.Y);
                var vp = CluwneLib.WorldViewport;

                // Get nearby lights
                var lights = IoCManager.Resolve<ILightManager>().LightsIntersectingRect(vp);

                // Render the lightmap
                RenderLightsIntoMap(lights);
                CalculateSceneBatches(vp);


                //Draw all rendertargets to the scenetarget
                _sceneTarget.BeginDrawing();
                _sceneTarget.Clear(Color.Black);

                //PreOcclusion
                RenderTiles();

                RenderComponents(e.FrameDeltaTime, vp);

                RenderOverlay();


                _sceneTarget.EndDrawing();
                _sceneTarget.ResetCurrentRenderTarget();

                //Debug.DebugRendertarget(_sceneTarget);

                if (bFullVision)
                    _sceneTarget.Blit(0, 0, CluwneLib.Screen.Size.X, CluwneLib.Screen.Size.Y);
                else
                    LightScene();


                RenderDebug(vp);

                //Render the placement manager shit
                PlacementManager.Render();
            }
        }

        private void RenderTiles()
        {
            if (_redrawTiles)
            {
                //Set rendertarget to draw the rest of the scene
                _tilesTarget.BeginDrawing();
                _tilesTarget.Clear(Color.Black);

                if (_floorBatch.Count > 0)
                {
                    _tilesTarget.Draw(_floorBatch);
                }

                if (_wallBatch.Count > 0)
                    _tilesTarget.Draw(_wallBatch);

                if (_wallTopsBatch.Count > 0)
                    _overlayTarget.Draw(_wallTopsBatch);

                _tilesTarget.EndDrawing();
                _redrawTiles = false;
            }

            _tilesTarget.Blit(0, 0, _tilesTarget.Width, _tilesTarget.Height, Color.White, BlitterSizeMode.Scale);
        }

        private void RenderOverlay()
        {
            if (_redrawOverlay)
            {
                _overlayTarget.BeginDrawing();
                _overlayTarget.Clear(Color.Transparent);

                // Render decal batch

                if (_decalBatch.Count > 0)
                    _overlayTarget.Draw(_decalBatch);

                if (_gasBatch.Count > 0)
                    _overlayTarget.Draw(_gasBatch);

                _redrawOverlay = false;
                _overlayTarget.EndDrawing();
            }

            _overlayTarget.Blit(0, 0, _tilesTarget.Width, _tilesTarget.Height, Color.White, BlitterSizeMode.Crop);
        }

        private void RenderDebug(FloatRect viewport)
        {
            if (debugWallOccluders || debugPlayerShadowMap)
                _occluderDebugTarget.Blit(0, 0, _occluderDebugTarget.Width / 4, _occluderDebugTarget.Height / 4, Color.White, BlitterSizeMode.Scale);

            if (CluwneLib.Debug.DebugColliders)
            {
                var colliders =
                    _entityManager.ComponentManager.GetComponents(ComponentFamily.Collider)
                    .OfType<ColliderComponent>()
                    .Select(c => new { Color = c.DebugColor, AABB = c.WorldAABB })
                    .Where(c => !c.AABB.IsEmpty() && c.AABB.Intersects(viewport));

                var collidables =
                    _entityManager.ComponentManager.GetComponents(ComponentFamily.Collidable)
                    .OfType<CollidableComponent>()
                    .Select(c => new { Color = c.DebugColor, AABB = c.AABB })
                    .Where(c => !c.AABB.IsEmpty() && c.AABB.Intersects(viewport));

                foreach (var hitbox in colliders.Concat(collidables))
                {
                    var box = CluwneLib.WorldToScreen(hitbox.AABB);
                    CluwneLib.drawRectangle((int)box.Left, (int)box.Top, (int)box.Width, (int)box.Height,
                        hitbox.Color.WithAlpha(64));
                    CluwneLib.drawHollowRectangle((int)box.Left, (int)box.Top, (int)box.Width, (int)box.Height, 1f,
                        hitbox.Color.WithAlpha(128));
                }
            }
            if (CluwneLib.Debug.DebugGridDisplay)
            {
                int startX = 10;
                int startY = 10;
                CluwneLib.drawRectangle(startX, startY, 200, 300,
                        Color.Blue.WithAlpha(64));

                // Player position debug
                Vector2f playerWorldOffset = PlayerManager.ControlledEntity.GetComponent<TransformComponent>(ComponentFamily.Transform).Position;
                Vector2f playerTile = CluwneLib.WorldToTile(playerWorldOffset);
                Vector2f playerScreen = CluwneLib.WorldToScreen(playerWorldOffset);
                CluwneLib.drawText(15, 15, "Postioning Debug", 14, Color.White);
                CluwneLib.drawText(15, 30, "Character Pos", 14, Color.White);
                CluwneLib.drawText(15, 45, String.Format("Pixel: {0} / {1}", playerWorldOffset.X, playerWorldOffset.Y), 14, Color.White);
                CluwneLib.drawText(15, 60, String.Format("World: {0} / {1}", playerTile.X, playerTile.Y), 14, Color.White);
                CluwneLib.drawText(15, 75, String.Format("Screen: {0} / {1}", playerScreen.X, playerScreen.Y), 14, Color.White);

                // Mouse position debug
                Vector2i mouseScreenPos = MousePosScreen; // default to screen space
                Vector2f mouseWorldOffset = CluwneLib.ScreenToWorld(MousePosScreen);
                Vector2f mouseTile = CluwneLib.WorldToTile(mouseWorldOffset);
                CluwneLib.drawText(15, 120, "Mouse Pos", 14, Color.White);
                CluwneLib.drawText(15, 135, String.Format("Pixel: {0} / {1}", mouseWorldOffset.X, mouseWorldOffset.Y), 14, Color.White);
                CluwneLib.drawText(15, 150, String.Format("World: {0} / {1}", mouseTile.X, mouseTile.Y), 14, Color.White);
                CluwneLib.drawText(15, 165, String.Format("Screen: {0} / {1}", mouseScreenPos.X, mouseScreenPos.Y), 14, Color.White);
            }
        }

        public void Shutdown()
        {
            IoCManager.Resolve<IPlayerManager>().Detach();

            _cleanupSpriteList.ForEach(s => s.Texture = null);
            _cleanupSpriteList.Clear();
            _cleanupList.ForEach(t => { t.Dispose(); });
            _cleanupList.Clear();

            shadowMapResolver.Dispose();
            _gaussianBlur.Dispose();
            _entityManager.Shutdown();
            UserInterfaceManager.DisposeAllComponents();
            NetworkManager.MessageArrived -= NetworkManagerMessageArrived;
            _decalBatch.Dispose();
            _floorBatch.Dispose();
            _gasBatch.Dispose();
            _wallBatch.Dispose();
            _wallTopsBatch.Dispose();
            GC.Collect();
        }


        #endregion

        #region Input

        #region Keyboard
        public void KeyPressed(KeyEventArgs e)
        {

        }

        public void KeyDown(KeyEventArgs e)
        {
            if (UserInterfaceManager.KeyDown(e)) //KeyDown returns true if the click is handled by the ui component.
                return;


            if (e.Code == Keyboard.Key.F1)
            {
                //TODO FrameStats
                CluwneLib.FrameStatsVisible = !CluwneLib.FrameStatsVisible;
            }
            if (e.Code == Keyboard.Key.F2)
            {
                _showDebug = !_showDebug;
                CluwneLib.Debug.ToggleWallDebug();
                CluwneLib.Debug.ToggleAABBDebug();
                CluwneLib.Debug.ToggleGridDisplayDebug();
            }
            if (e.Code == Keyboard.Key.F3)
            {
                ToggleOccluderDebug();
            }
            if (e.Code == Keyboard.Key.F4)
            {
                debugHitboxes = !debugHitboxes;
            }
            if (e.Code == Keyboard.Key.F5)
            {
                PlayerManager.SendVerb("save", 0);
            }
            if (e.Code == Keyboard.Key.F6)
            {
                bFullVision = !bFullVision;
            }
            if (e.Code == Keyboard.Key.F7)
            {
                bPlayerVision = !bPlayerVision;
            }
            if (e.Code == Keyboard.Key.F8)
            {
                NetOutgoingMessage message = NetworkManager.CreateMessage();
                message.Write((byte)NetMessage.ForceRestart);
                NetworkManager.SendMessage(message, NetDeliveryMethod.ReliableUnordered);
            }
            if (e.Code == Keyboard.Key.Escape)
            {
                _menu.ToggleVisible();
            }
            if (e.Code == Keyboard.Key.F9)
            {
                UserInterfaceManager.ToggleMoveMode();
            }
            if (e.Code == Keyboard.Key.F10)
            {
                UserInterfaceManager.DisposeAllComponents<TileSpawnPanel>(); //Remove old ones.
                UserInterfaceManager.AddComponent(new TileSpawnPanel(new Vector2i(350, 410), ResourceManager,
                                                                     PlacementManager)); //Create a new one.
            }
            if (e.Code == Keyboard.Key.F11)
            {
                UserInterfaceManager.DisposeAllComponents<EntitySpawnPanel>(); //Remove old ones.
                UserInterfaceManager.AddComponent(new EntitySpawnPanel(new Vector2i(350, 410), ResourceManager,
                                                                       PlacementManager)); //Create a new one.
            }

            PlayerManager.KeyDown(e.Code);
        }

        public void KeyUp(KeyEventArgs e)
        {
            PlayerManager.KeyUp(e.Code);
        }

        public void TextEntered(TextEventArgs e)
        {
            UserInterfaceManager.TextEntered(e);
        }
        #endregion

        #region Mouse
        public void MouseUp(MouseButtonEventArgs e)
        {
            UserInterfaceManager.MouseUp(e);
        }

        public void MouseDown(MouseButtonEventArgs e)
        {
            if (PlayerManager.ControlledEntity == null)
                return;

            if (UserInterfaceManager.MouseDown(e))
                // MouseDown returns true if the click is handled by the ui component.
                return;

            if (PlacementManager.IsActive && !PlacementManager.Eraser)
            {
                switch (e.Button)
                {
                    case Mouse.Button.Left:
                        PlacementManager.HandlePlacement();
                        return;
                    case Mouse.Button.Right:
                        PlacementManager.Clear();
                        return;
                    case Mouse.Button.Middle:
                        PlacementManager.Rotate();
                        return;
                }
            }

            #region Object clicking

            // Convert our click from screen -> world coordinates
            //Vector2 worldPosition = new Vector2(e.Position.X + xTopLeft, e.Position.Y + yTopLeft);
            float checkDistance = 1.5f;
            // Find all the entities near us we could have clicked
            Entity[] entities =
                ((EntityManager)IoCManager.Resolve<IEntityManagerContainer>().EntityManager).GetEntitiesInRange(
                    PlayerManager.ControlledEntity.GetComponent<TransformComponent>(ComponentFamily.Transform).Position,
                    checkDistance);

            // See which one our click AABB intersected with
            var clickedEntities = new List<ClickData>();
            var clickedWorldPoint = new Vector2f(MousePosWorld.X, MousePosWorld.Y);
            foreach (Entity entity in entities)
            {
                var clickable = (ClickableComponent)entity.GetComponent(ComponentFamily.Click);
                if (clickable == null) continue;
                int drawdepthofclicked;
                if (clickable.CheckClick(clickedWorldPoint, out drawdepthofclicked))
                    clickedEntities.Add(new ClickData(entity, drawdepthofclicked));
            }

            if (clickedEntities.Any())
            {
                //var entToClick = (from cd in clickedEntities                       //Treat mobs and their clothes as on the same level as ground placeables (windows, doors)
                //                  orderby (cd.Drawdepth == (int)DrawDepth.MobBase ||//This is a workaround to make both windows etc. and objects that rely on layers (objects on tables) work.
                //                            cd.Drawdepth == (int)DrawDepth.MobOverAccessoryLayer ||
                //                            cd.Drawdepth == (int)DrawDepth.MobOverClothingLayer ||
                //                            cd.Drawdepth == (int)DrawDepth.MobUnderAccessoryLayer ||
                //                            cd.Drawdepth == (int)DrawDepth.MobUnderClothingLayer
                //                   ? (int)DrawDepth.FloorPlaceable : cd.Drawdepth) ascending, cd.Clicked.Position.Y ascending
                //                  select cd.Clicked).Last();

                Entity entToClick = (from cd in clickedEntities
                                     orderby cd.Drawdepth ascending,
                                         cd.Clicked.GetComponent<TransformComponent>(ComponentFamily.Transform).Position
                                         .Y ascending
                                     select cd.Clicked).Last();

                if (PlacementManager.Eraser && PlacementManager.IsActive)
                {
                    PlacementManager.HandleDeletion(entToClick);
                    return;
                }

                ClickableComponent c;
                switch (e.Button)
                {
                    case Mouse.Button.Left:
                        c = (ClickableComponent)entToClick.GetComponent(ComponentFamily.Click);
                        c.DispatchClick(PlayerManager.ControlledEntity.Uid, MouseClickType.Left);
                        break;
                    case Mouse.Button.Right:
                        c = (ClickableComponent)entToClick.GetComponent(ComponentFamily.Click);
                        c.DispatchClick(PlayerManager.ControlledEntity.Uid, MouseClickType.Right);
                        break;
                    case Mouse.Button.Middle:
                        UserInterfaceManager.DisposeAllComponents<PropEditWindow>();
                        UserInterfaceManager.AddComponent(new PropEditWindow(new Vector2i(400, 400), ResourceManager,
                                                                             entToClick));
                        break;
                }
            }

            #endregion
        }

        public void MouseMove(MouseMoveEventArgs e)
        {
            MousePosScreen = new Vector2i(e.X, e.Y);
            MousePosWorld = CluwneLib.ScreenToWorld(MousePosScreen);
            UserInterfaceManager.MouseMove(e);
        }

        public void MouseMoved(MouseMoveEventArgs e)
        {

        }

        public void MousePressed(MouseButtonEventArgs e)
        {

        }

        public void MouseWheelMove(MouseWheelEventArgs e)
        {
            UserInterfaceManager.MouseWheelMove(e);
        }

        public void MouseEntered(EventArgs e)
        {
            UserInterfaceManager.MouseEntered(e);
        }

        public void MouseLeft(EventArgs e)
        {
            UserInterfaceManager.MouseLeft(e);
        }
        #endregion

        #region Chat
        private void HandleChatMessage(NetIncomingMessage msg)
        {
            var channel = (ChatChannel)msg.ReadByte();
            string text = msg.ReadString();
            int entityId = msg.ReadInt32();
            string message;
            switch (channel)
            {
                /*case ChatChannel.Emote:
                message = _entityManager.GetEntity(entityId).Name + " " + text;
                break;
            case ChatChannel.Damage:
                message = text;
                break; //Formatting is handled by the server. */
                case ChatChannel.Ingame:
                case ChatChannel.Server:
                case ChatChannel.OOC:
                case ChatChannel.Radio:
                    message = "[" + channel + "] " + text;
                    break;
                default:
                    message = text;
                    break;
            }
            _gameChat.AddLine(message, channel);
            if (entityId > 0)
            {
                Entity a = IoCManager.Resolve<IEntityManagerContainer>().EntityManager.GetEntity(entityId);
                if (a != null)
                {
                    a.SendMessage(this, ComponentMessageType.EntitySaidSomething, channel, text);
                }
            }
        }

        private void ChatTextboxTextSubmitted(Chatbox chatbox, string text)
        {
            SendChatMessage(text);
        }

        private void SendChatMessage(string text)
        {
            NetOutgoingMessage message = NetworkManager.CreateMessage();
            message.Write((byte)NetMessage.ChatMessage);
            message.Write((byte)ChatChannel.Player);
            message.Write(text);
            NetworkManager.SendMessage(message, NetDeliveryMethod.ReliableUnordered);
        }

        #endregion

        #endregion

        #region Event Handlers

        #region Buttons
        private void menuButton_Clicked(ImageButton sender)
        {
            _menu.ToggleVisible();
        }

        private void statusButton_Clicked(ImageButton sender)
        {
            UserInterfaceManager.ComponentUpdate(GuiComponentType.ComboGui, ComboGuiMessage.ToggleShowPage, 2);
        }

        private void inventoryButton_Clicked(ImageButton sender)
        {
            UserInterfaceManager.ComponentUpdate(GuiComponentType.ComboGui, ComboGuiMessage.ToggleShowPage, 1);
        }

        #endregion

        #region Messages

        private void NetworkManagerMessageArrived(object sender, IncomingNetworkMessageArgs args)
        {
            NetIncomingMessage message = args.Message;
            if (message == null)
            {
                return;
            }
            switch (message.MessageType)
            {
                case NetIncomingMessageType.StatusChanged:
                    var statMsg = (NetConnectionStatus)message.ReadByte();
                    if (statMsg == NetConnectionStatus.Disconnected)
                    {
                        string disconnectMessage = message.ReadString();
                        UserInterfaceManager.AddComponent(new DisconnectedScreenBlocker(StateManager,
                                                                                        UserInterfaceManager,
                                                                                        ResourceManager,
                                                                                        disconnectMessage));
                    }
                    break;
                case NetIncomingMessageType.Data:
                    var messageType = (NetMessage)message.ReadByte();
                    switch (messageType)
                    {
                        case NetMessage.MapMessage:
                            MapManager.HandleNetworkMessage(message);
                            break;
                        //case NetMessage.AtmosDisplayUpdate:
                        //    MapManager.HandleAtmosDisplayUpdate(message);
                        //    break;
                        case NetMessage.PlayerSessionMessage:
                            PlayerManager.HandleNetworkMessage(message);
                            break;
                        case NetMessage.PlayerUiMessage:
                            UserInterfaceManager.HandleNetMessage(message);
                            break;
                        case NetMessage.PlacementManagerMessage:
                            PlacementManager.HandleNetMessage(message);
                            break;
                        case NetMessage.ChatMessage:
                            HandleChatMessage(message);
                            break;
                        case NetMessage.EntityMessage:
                            _entityManager.HandleEntityNetworkMessage(message);
                            break;
                        case NetMessage.StateUpdate:
                            HandleStateUpdate(message);
                            break;
                        case NetMessage.FullState:
                            HandleFullState(message);
                            break;
                    }
                    break;
            }
        }

        #endregion

        #region State

        /// <summary>
        /// HandleStateUpdate
        ///
        /// Recieves a state update message and unpacks the delicious GameStateDelta hidden inside
        /// Then it applies the gamestatedelta to a past state to form: a full game state!
        /// </summary>
        /// <param name="message">incoming state update message</param>
        private void HandleStateUpdate(NetIncomingMessage message)
        {
            //Read the delta from the message
            GameStateDelta delta = GameStateDelta.ReadDelta(message);

            if (!_lastStates.ContainsKey(delta.FromSequence)) // Drop messages that reference a state that we don't have
                return; //TODO request full state here?

            //Acknowledge reciept before we do too much more shit -- ack as quickly as possible
            SendStateAck(delta.Sequence);

            //Grab the 'from' state
            GameState fromState = _lastStates[delta.FromSequence];
            //Apply the delta
            LogManager.Log("Applying delta of size " + delta.Size.ToString());
            GameState newState = fromState + delta;
            newState.GameTime = IoCManager.Resolve<IGameTimer>().CurrentTime;

            // Go ahead and store it even if our current state is newer than this one, because
            // a newer state delta may later reference this one.
            _lastStates[delta.Sequence] = newState;

            if (delta.Sequence > _currentStateSequence)
                _currentStateSequence = delta.Sequence;

            ApplyCurrentGameState();

            //Dump states that have passed out of being relevant
            CullOldStates(delta.FromSequence);
        }

        /// <summary>
        /// CullOldStates
        ///
        /// Deletes states that are no longer relevant
        /// </summary>
        /// <param name="sequence">state sequence number</param>
        private void CullOldStates(uint sequence)
        {
            foreach (uint v in _lastStates.Keys.Where(v => v < sequence).ToList())
                _lastStates.Remove(v);
        }

        /// <summary>
        /// HandleFullState
        ///
        /// Handles full gamestates - for initializing.
        /// </summary>
        /// <param name="message">incoming full state message</param>
        private void HandleFullState(NetIncomingMessage message)
        {
            GameState newState = GameState.ReadStateMessage(message);
            newState.GameTime = IoCManager.Resolve<IGameTimer>().CurrentTime;
            SendStateAck(newState.Sequence);

            //Store the new state
            _lastStates[newState.Sequence] = newState;
            _currentStateSequence = newState.Sequence;
            ApplyCurrentGameState();
        }

        private void ApplyCurrentGameState()
        {
            GameState currentState = _lastStates[_currentStateSequence];
            _entityManager.ApplyEntityStates(currentState.EntityStates, currentState.GameTime);
            PlayerManager.ApplyPlayerStates(currentState.PlayerStates);
        }

        /// <summary>
        /// SendStateAck
        ///
        /// Acknowledge a game state being recieved
        /// </summary>
        /// <param name="sequence">State sequence number</param>
        private void SendStateAck(uint sequence)
        {
            NetOutgoingMessage message = NetworkManager.CreateMessage();
            message.Write((byte)NetMessage.StateAck);
            message.Write(sequence);
            NetworkManager.SendMessage(message, NetDeliveryMethod.Unreliable);
        }

        public void FormResize()
        {
            CluwneLib.ScreenViewportSize =
                new Vector2u(CluwneLib.Screen.Size.X, CluwneLib.Screen.Size.Y);

            UserInterfaceManager.ResizeComponents();
            ResetRendertargets();
            IoCManager.Resolve<ILightManager>().RecalculateLights();
            RecalculateScene();
        }

        #endregion

        private void OnPlayerMove(object sender, VectorEventArgs args)
        {
            //Recalculate scene batches for drawing.
            RecalculateScene();
        }

        public void OnTileChanged(TileRef tileRef, Tile oldTile)
        {
            IoCManager.Resolve<ILightManager>().RecalculateLightsInView(new FloatRect(tileRef.X, tileRef.Y, 1, 1));
            // Recalculate the scene batches.
            RecalculateScene();
        }

        #endregion

        #region Lighting in order of call

        /**
         *  Calculate lights In player view
         *  Render Lights in view to lightmap  > Screenshadows
         *
         *
         **/


        private void CalculateAllLights()
        {
            foreach
            (ILight l in IoCManager.Resolve<ILightManager>().GetLights().Where(l => l.LightArea.Calculated == false))
            {
                CalculateLightArea(l);
            }
        }

        /// <summary>
        /// Renders a set of lights into a single lightmap.
        /// If a light hasn't been prerendered yet, it renders that light.
        /// </summary>
        /// <param name="lights">Array of lights</param>
        private void RenderLightsIntoMap(IEnumerable<ILight> lights)
        {

            //Step 1 - Calculate lights that haven't been calculated yet or need refreshing
            foreach (ILight l in lights.Where(l => l.LightArea.Calculated == false))
            {
                if (l.LightState != LightState.On)
                    continue;
                //Render the light area to its own target.
                CalculateLightArea(l);
            }

            //Step 2 - Set up the render targets for the composite lighting.
            screenShadows.Clear(Color.Black);

            RenderImage source = screenShadows;
            source.Clear(Color.Black);

            RenderImage desto = shadowIntermediate;
            RenderImage copy = null;

            Lightmap.setAsCurrentShader();

            var lightTextures = new List<Texture>();
            var colors = new List<Vector4f>();
            var positions = new List<Vector4f>();

            //Step 3 - Blend all the lights!
            foreach (ILight Light in lights)
            {
                //Skip off or broken lights (TODO code broken light states)
                if (Light.LightState != LightState.On)
                    continue;

                // LIGHT BLEND STAGE 1 - SIZING -- copys the light texture to a full screen rendertarget
                var area = (LightArea)Light.LightArea;

                //Set the drawing position.
                Vector2f blitPos = CluwneLib.WorldToScreen(area.LightPosition) - area.LightAreaSize * 0.5f;


                //Set shader parameters
                var LightPositionData = new Vector4f(blitPos.X / screenShadows.Width,
                                                    blitPos.Y / screenShadows.Height,
                                                    (float)screenShadows.Width / area.RenderTarget.Width,
                                                    (float)screenShadows.Height / area.RenderTarget.Height);
                lightTextures.Add(area.RenderTarget.Texture);
                colors.Add(Light.GetColorVec());
                positions.Add(LightPositionData);
            }
            int i = 0;
            int num_lights = 6;
            bool draw = false;
            bool fill = false;
            Texture black = IoCManager.Resolve<IResourceManager>().GetSprite("black5x5").Texture;
            var r_img = new Texture[num_lights];
            var r_col = new Vector4f[num_lights];
            var r_pos = new Vector4f[num_lights];
            do
            {
                if (fill)
                {
                    for (int j = i; j < num_lights; j++)
                    {
                        r_img[j] = black;
                        r_col[j] = Vector4f.Zero;
                        r_pos[j] = new Vector4f(0, 0, 1, 1);
                    }
                    i = num_lights;
                    draw = true;
                    fill = false;
                }
                if (draw)
                {

                    desto.BeginDrawing();

                    Lightmap.SetParameter("LightPosData", r_pos);
                    Lightmap.SetParameter("Colors", r_col);
                    Lightmap.SetParameter("light0", r_img[0]);
                    Lightmap.SetParameter("light1", r_img[1]);
                    Lightmap.SetParameter("light2", r_img[2]);
                    Lightmap.SetParameter("light3", r_img[3]);
                    Lightmap.SetParameter("light4", r_img[4]);
                    Lightmap.SetParameter("light5", r_img[5]);
                    Lightmap.SetParameter("sceneTexture", source);

                    // Blit the shadow image on top of the screen
                    source.Blit(0, 0, source.Width, source.Height, BlitterSizeMode.Crop);

                    desto.EndDrawing();


                    //Swap rendertargets to set up for the next light
                    copy = source;
                    source = desto;
                    desto = copy;
                    i = 0;

                    draw = false;
                    fill = false;
                    r_img = new Texture[num_lights];
                    r_col = new Vector4f[num_lights];
                    r_pos = new Vector4f[num_lights];
                }
                if (lightTextures.Count > 0)
                {
                    r_img[i] = lightTextures[0];
                    lightTextures.RemoveAt(0);


                    r_col[i] = colors[0];
                    colors.RemoveAt(0);

                    r_pos[i] = positions[0];
                    positions.RemoveAt(0);

                    i++;
                }
                if (i == num_lights)
                    //if I is equal to 6 draw
                    draw = true;
                if (i > 0 && i < num_lights && lightTextures.Count == 0)
                    // If all light textures in lightTextures have been processed, fill = true
                    fill = true;
            } while (lightTextures.Count > 0 || draw || fill);

            Lightmap.ResetCurrentShader();

            if(source != screenShadows)
            {
                screenShadows.BeginDrawing();
                source.Blit(0, 0, source.Width, source.Height);
                screenShadows.EndDrawing();
            }

            var texunflipx = screenShadows.Texture.CopyToImage();
            texunflipx.FlipVertically();
            screenShadows.Texture.Update(texunflipx);
        }

        private void CalculateSceneBatches(FloatRect vision)
        {
            if (!_recalculateScene)
                return;

            // Render the player sightline occluder
            RenderPlayerVisionMap();

            //Blur the player vision map
            BlurPlayerVision();

            _decalBatch.BeginDrawing();
            _wallTopsBatch.BeginDrawing();
            _floorBatch.BeginDrawing();
            _wallBatch.BeginDrawing();
            _gasBatch.BeginDrawing();

            DrawTiles(vision);

            _floorBatch.EndDrawing();
            _decalBatch.EndDrawing();
            _wallTopsBatch.EndDrawing();
            _gasBatch.EndDrawing();
            _wallBatch.EndDrawing();

            _recalculateScene = false;
            _redrawTiles = true;
            _redrawOverlay = true;
        }

        private void RenderPlayerVisionMap()
        {


            if (bFullVision)
            {
                playerOcclusionTarget.Clear(new SFML.Graphics.Color(211, 211, 211));
                return;
            }
            if (bPlayerVision)
            {
                // I think this should be transparent? Maybe it should be black for the player occlusion...
                // I don't remember. --volundr
                playerOcclusionTarget.Clear(Color.Black);
                playerVision.Move(PlayerManager.ControlledEntity.GetComponent<TransformComponent>(ComponentFamily.Transform).Position);


                LightArea area =  GetLightArea(RadiusToShadowMapSize( playerVision.Radius));
                area.LightPosition =  playerVision.Position  ; // Set the light position

                TileRef TileReference = MapManager.GetTileRef(playerVision.Position);

                if (TileReference.Tile.TileDef.IsOpaque)
                {

                    area.LightPosition = new Vector2f(area.LightPosition.X, TileReference.Y + MapManager.TileSize + 1);


                }


                area.BeginDrawingShadowCasters(); // Start drawing to the light rendertarget
                DrawWallsRelativeToLight(area); // Draw all shadowcasting stuff here in black
                area.EndDrawingShadowCasters(); // End drawing to the light rendertarget

                Vector2f blitPos = CluwneLib.WorldToScreen(area.LightPosition) - area.LightAreaSize * 0.5f;
                var tmpBlitPos = CluwneLib.WorldToScreen(area.LightPosition) -
                                 new Vector2f(area.RenderTarget.Width, area.RenderTarget.Height) * 0.5f;

                if (debugWallOccluders)
                {

                    _occluderDebugTarget.BeginDrawing();
                    _occluderDebugTarget.Clear(Color.White);
                    area.RenderTarget.Blit((int)tmpBlitPos.X, (int)tmpBlitPos.Y, area.RenderTarget.Width, area.RenderTarget.Height,
                        Color.White, BlitterSizeMode.Crop);
                    _occluderDebugTarget.EndDrawing();

                }

                shadowMapResolver.ResolveShadows(area, false, IoCManager.Resolve<IResourceManager>().GetSprite("whitemask").Texture); // Calc shadows

                if (debugPlayerShadowMap)
                {
                    _occluderDebugTarget.BeginDrawing();
                    _occluderDebugTarget.Clear(Color.White);
                    area.RenderTarget.Blit((int)tmpBlitPos.X, (int)tmpBlitPos.Y, area.RenderTarget.Width, area.RenderTarget.Height, Color.White, BlitterSizeMode.Crop);
                    _occluderDebugTarget.EndDrawing();
                }

                playerOcclusionTarget.BeginDrawing(); // Set to shadow rendertarget

                //area.renderTarget.SourceBlend = AlphaBlendOperation.One;
                //area.renderTarget.DestinationBlend = AlphaBlendOperation.Zero;
                area.RenderTarget.BlendSettings.ColorSrcFactor = BlendMode.Factor.One;
                area.RenderTarget.BlendSettings.ColorDstFactor = BlendMode.Factor.Zero;

                area.RenderTarget.Blit((int)blitPos.X, (int)blitPos.Y, area.RenderTarget.Width, area.RenderTarget.Height, Color.White, BlitterSizeMode.Crop);

                //area.renderTarget.SourceBlend = AlphaBlendOperation.SourceAlpha; //reset blend mode
                //area.renderTarget.DestinationBlend = AlphaBlendOperation.InverseSourceAlpha; //reset blend mode
                area.RenderTarget.BlendSettings.ColorDstFactor = BlendMode.Factor.SrcAlpha;
                area.RenderTarget.BlendSettings.ColorDstFactor = BlendMode.Factor.OneMinusSrcAlpha;

                playerOcclusionTarget.EndDrawing();


                //Debug.DebugRendertarget(playerOcclusionTarget);


            }
            else
            {
                playerOcclusionTarget.Clear(Color.Black);
            }
        }

        // Draws all walls in the area around the light relative to it, and in black (test code, not pretty)
        private void DrawWallsRelativeToLight(ILightArea area)
        {
            Vector2f lightAreaSize = CluwneLib.PixelToTile(area.LightAreaSize) / 2;
            var lightArea = new FloatRect(area.LightPosition - lightAreaSize, CluwneLib.PixelToTile(area.LightAreaSize));

            var tiles = MapManager.GetWallsIntersecting(lightArea);

            foreach (TileRef t in tiles)
            {
                Vector2f pos = area.ToRelativePosition(CluwneLib.WorldToScreen(new Vector2f(t.X, t.Y)));
                t.Tile.TileDef.RenderPos(pos.X, pos.Y);
            }
        }

        private void BlurPlayerVision()
        {
            _gaussianBlur.SetRadius(11);
            _gaussianBlur.SetAmount(2);
            _gaussianBlur.SetSize(new Vector2f(playerOcclusionTarget.Width, playerOcclusionTarget.Height));
            _gaussianBlur.PerformGaussianBlur(playerOcclusionTarget);
        }

        /// <summary>
        /// Copys all tile sprites into batches.
        /// </summary>
        private void DrawTiles(FloatRect vision)
        {
            var tiles = MapManager.GetTilesIntersecting(vision, false);
            var walls = new List<TileRef>();

            foreach (TileRef TileReference in tiles)
            {
                var Tile = TileReference.Tile;
                var TileType = Tile.TileDef;

                //t.RenderGas(WindowOrigin.X, WindowOrigin.Y, tilespacing, _gasBatch);
                if (TileType.IsWall)
                    walls.Add(TileReference);
                else
                {
                    var point = CluwneLib.WorldToScreen(new Vector2f(TileReference.X, TileReference.Y));
                    TileType.Render(point.X, point.Y, _floorBatch);
                    TileType.RenderGas(point.X, point.Y, MapManager.TileSize, _gasBatch);
                }

            }

            walls.Sort((t1, t2) => t1.Y - t2.Y);

            foreach (TileRef tr in walls)
            {
                var t = tr.Tile;
                var td = t.TileDef;

                var point = CluwneLib.WorldToScreen(new Vector2f(tr.X, tr.Y));
                td.Render(point.X, point.Y, _wallBatch);
                td.RenderTop(point.X, point.Y, _wallTopsBatch);
            }
        }

        /// <summary>
        /// Render the renderables
        /// </summary>
        /// <param name="frametime">time since the last frame was rendered.</param>
        private void RenderComponents(float frameTime, FloatRect viewPort)
        {
            IEnumerable<Component> components = _entityManager.ComponentManager.GetComponents(ComponentFamily.Renderable)
                .Union(_entityManager.ComponentManager.GetComponents(ComponentFamily.Particles));

            IEnumerable<IRenderableComponent> floorRenderables = from IRenderableComponent c in components
                                                                 orderby c.Bottom ascending, c.DrawDepth ascending
                                                                 where c.DrawDepth < DrawDepth.MobBase
                                                                 select c;

            RenderList(new Vector2f(viewPort.Left, viewPort.Top), new Vector2f(viewPort.Right(), viewPort.Bottom()),
                       floorRenderables);

            IEnumerable<IRenderableComponent> largeRenderables = from IRenderableComponent c in components
                                                                 orderby c.Bottom ascending
                                                                 where c.DrawDepth >= DrawDepth.MobBase &&
                                                                       c.DrawDepth < DrawDepth.WallTops
                                                                 select c;

            RenderList(new Vector2f(viewPort.Left, viewPort.Top), new Vector2f(viewPort.Right(), viewPort.Bottom()),
                       largeRenderables);

            IEnumerable<IRenderableComponent> ceilingRenderables = from IRenderableComponent c in components
                                                                   orderby c.Bottom ascending, c.DrawDepth ascending
                                                                   where c.DrawDepth >= DrawDepth.WallTops
                                                                   select c;

            RenderList(new Vector2f(viewPort.Left, viewPort.Top), new Vector2f(viewPort.Right(), viewPort.Bottom()),
                       ceilingRenderables);
        }

        private void LightScene()
        {

            //Blur the light/shadow map
            BlurShadowMap();

            //Render the scene and lights together to compose the lit scene

            _composedSceneTarget.BeginDrawing();
            _composedSceneTarget.Clear(Color.Black);
            LightblendTechnique["FinalLightBlend"].setAsCurrentShader();
            Sprite outofview = IoCManager.Resolve<IResourceManager>().GetSprite("outofview");
            float texratiox = (float)CluwneLib.CurrentClippingViewport.Width / outofview.Texture.Size.X;
            float texratioy = (float)CluwneLib.CurrentClippingViewport.Height / outofview.Texture.Size.Y;
            var maskProps = new Vector4f(texratiox, texratioy, 0, 0);

            LightblendTechnique["FinalLightBlend"].SetParameter("PlayerViewTexture", playerOcclusionTarget);
            LightblendTechnique["FinalLightBlend"].SetParameter("OutOfViewTexture", outofview.Texture);
            LightblendTechnique["FinalLightBlend"].SetParameter("MaskProps", maskProps);
            LightblendTechnique["FinalLightBlend"].SetParameter("LightTexture", screenShadows);
            LightblendTechnique["FinalLightBlend"].SetParameter("SceneTexture", _sceneTarget);
            LightblendTechnique["FinalLightBlend"].SetParameter("AmbientLight", new Vector4f(.05f, .05f, 0.05f, 1));


            // Blit the shadow image on top of the screen
            screenShadows.Blit(0, 0, screenShadows.Width, screenShadows.Height, Color.White, BlitterSizeMode.Crop);


            LightblendTechnique["FinalLightBlend"].ResetCurrentShader();
            _composedSceneTarget.EndDrawing();

         //  Debug.DebugRendertarget(_composedSceneTarget);

            playerOcclusionTarget.ResetCurrentRenderTarget(); // set the rendertarget back to screen
            playerOcclusionTarget.Blit(0, 0, screenShadows.Width, screenShadows.Height, Color.White, BlitterSizeMode.Crop); //draw playervision again
            PlayerPostProcess();

            //redraw composed scene
            _composedSceneTarget.Blit(0, 0, (uint)CluwneLib.Screen.Size.X, (uint)CluwneLib.Screen.Size.Y, Color.White, BlitterSizeMode.Crop);




            //old
            //   screenShadows.Blit(0, 0, _tilesTarget.Width, _tilesTarget.Height, Color.White, BlitterSizeMode.Crop);
            //   playerOcclusionTarget.Blit(0, 0, _tilesTarget.Width, _tilesTarget.Height, Color.White, BlitterSizeMode.Crop);

        }

        private void BlurShadowMap()
        {
            _gaussianBlur.SetRadius(11);
            _gaussianBlur.SetAmount(2);
            _gaussianBlur.SetSize(new Vector2f(screenShadows.Width, screenShadows.Height));
            _gaussianBlur.PerformGaussianBlur(screenShadows);
        }

        private void PlayerPostProcess()
        {
            PlayerManager.ApplyEffects(_composedSceneTarget);
        }

        #endregion

        #region Helper methods

        private void RenderList(Vector2f topleft, Vector2f bottomright, IEnumerable<IRenderableComponent> renderables)
        {
            foreach (IRenderableComponent component in renderables)
            {
                if (component is SpriteComponent)
                {
                    //Slaved components are drawn by their master
                    var c = component as SpriteComponent;
                    if (c.IsSlaved())
                        continue;
                }
                component.Render(topleft, bottomright);
            }
        }

        private void CalculateLightArea(ILight light)
        {

            ILightArea area = light.LightArea;
            if (area.Calculated)
                return;
            area.LightPosition = light.Position; //mousePosWorld; // Set the light position
            TileRef t = MapManager.GetTileRef(light.Position);
            if (t.Tile.IsSpace)
                return;
            if (t.Tile.TileDef.IsOpaque)
            {
                area.LightPosition = new Vector2f(area.LightPosition.X,
                                                  t.Y +
                                                  MapManager.TileSize + 1);
            }
            area.BeginDrawingShadowCasters(); // Start drawing to the light rendertarget
            DrawWallsRelativeToLight(area); // Draw all shadowcasting stuff here in black
            area.EndDrawingShadowCasters(); // End drawing to the light rendertarget
            shadowMapResolver.ResolveShadows((LightArea)area, true); // Calc shadows
            area.Calculated = true;
        }

        private ShadowmapSize RadiusToShadowMapSize(int Radius)
        {
            switch (Radius)
            {
                case 128:
                    return ShadowmapSize.Size128;
                case 256:
                    return ShadowmapSize.Size256;
                case 512:
                    return ShadowmapSize.Size512;
                case 1024:
                    return ShadowmapSize.Size1024;
                default:
                    return ShadowmapSize.Size1024;
            }
        }

        private LightArea GetLightArea(ShadowmapSize size)
        {
            switch (size)
            {
                case ShadowmapSize.Size128:
                    return lightArea128;
                case ShadowmapSize.Size256:
                    return lightArea256;
                case ShadowmapSize.Size512:
                    return lightArea512;
                case ShadowmapSize.Size1024:
                    return lightArea1024;
                default:
                    return lightArea1024;
            }

        }

        private void RecalculateScene()
        {
            _recalculateScene = true;
        }

        private void ResetRendertargets()
        {
            foreach (var rt in _cleanupList)
                rt.Dispose();
            foreach (var sp in _cleanupSpriteList)
                sp.Dispose();

            InitializeRenderTargets();
            InitalizeLighting();
        }

        private void ToggleOccluderDebug()
        {
            debugWallOccluders = !debugWallOccluders;
        }



        #endregion

        #region Nested type: ClickData

        private struct ClickData
        {
            public readonly Entity Clicked;
            public readonly int Drawdepth;

            public ClickData(Entity clicked, int drawdepth)
            {
                Clicked = clicked;
                Drawdepth = drawdepth;
            }
        }

        #endregion
    }
}
