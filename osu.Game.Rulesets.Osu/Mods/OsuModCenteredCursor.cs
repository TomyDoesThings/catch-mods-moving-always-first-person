// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Linq;
using System.Threading.Tasks;
using osu.Framework.Bindables;
using osu.Framework.Extensions.ObjectExtensions;
using osu.Framework.Graphics;
using osu.Framework.Input;
using osu.Framework.Input.Events;
using osu.Framework.Input.StateChanges;
using osu.Framework.Localisation;
using osu.Framework.Screens;
using osu.Game.Configuration;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Osu.Objects;
using osu.Game.Rulesets.Osu.UI;
using osu.Game.Rulesets.UI;
using osu.Game.Screens.Play;
using osu.Game.Storyboards;
using osu.Game.Storyboards.Drawables;
using osuTK;

namespace osu.Game.Rulesets.Osu.Mods
{
    public partial class OsuModCenteredCursor : Mod, IReadFromConfig, IApplicableToDrawableRuleset<OsuHitObject>, IApplicableToPlayer
    {
        public override string Name => "Centered Cursor";
        public override string Acronym => "CC";
        public override LocalisableString Description => "Cursor stays in the middle!";
        public override ModType Type => ModType.Fun; // TODO: Mod icon (on next line), and IncompatibleMods base.IncompatibleMods.Concat vs fresh new[]
        public override Type[] IncompatibleMods => base.IncompatibleMods.Concat(new[] { typeof(OsuModFlashlight), typeof(OsuModAutopilot), typeof(OsuModRelax), typeof(OsuModBubbles), typeof(ModTouchDevice) }).ToArray();

        [SettingSource("Centred background", "Have the background follow.")] // From the perspective of the player w.r.t. cursor. Less ambiguous than e.g. "(un)adjusted"
        public BindableBool CentredBackground { get; } = new BindableBool();

        [SettingSource("Centred storyboard / video", "Have the storyboard / video follow.")] // From the perspective of the player w.r.t. cursor. Less ambiguous than e.g. "(un)adjusted"
        public BindableBool CentredStoryboard { get; } = new BindableBool();

        private readonly Bindable<bool> showStoryboard = new Bindable<bool>();

        public void ReadFromConfig(OsuConfigManager config)
        {
            config.BindWith(OsuSetting.ShowStoryboard, showStoryboard);
        }

        private OsuInputManager osuInputManager = null!;
        private DrawableOsuRuleset ruleset = null!;

        private OsuPlayfield playfield = null!;

        // osu!(standard) is two dimensions in note hitting, X and Y, so position adjustment logic is for Vector2 (X, Y)
        private static readonly Vector2 playfield_misc_pos_scale = new Vector2(1.6f); // Brute-forced, is magical, todo: may need more intricate calculation
        private Vector2 miscPos => playfield.Position * playfield_misc_pos_scale;

        public void ApplyToDrawableRuleset(DrawableRuleset<OsuHitObject> drawableRuleset)
        {
            ruleset = (DrawableOsuRuleset)drawableRuleset;

            playfield = ruleset.Playfield;

            osuInputManager = ruleset.KeyBindingInputManager;
        }

        private class ConvertedMousePositionAbsoluteInput : MousePositionAbsoluteInput;

        private partial class ExternalMousePosGetter : Drawable, IRequireHighFrequencyMousePosition
        {
            public bool Enable = true;
            public Vector2 MousePos { get; private set; } = Vector2.Zero;

            public override bool ReceivePositionalInputAt(Vector2 screenSpacePos) => true;

            protected override bool OnMouseMove(MouseMoveEvent e)
            {
                if (!Enable)
                    return base.OnMouseMove(e);

                // We skip our own added mouse position
                if (e.CurrentState.Mouse.LastSource is ConvertedMousePositionAbsoluteInput)
                    return base.OnMouseMove(e);

                MousePos = e.ScreenSpaceMousePosition;

                // We block real mouse position propagation to the playfield
                return true;
            }
        }

        private bool isExited(Player player) => !player.IsCurrentScreen(); // Inspired from TestScenePause's confirmExited

        private Drawable? getDrawableStoryboard(Player player) => player.DimmableStoryboard.Children.FirstOrDefault(d => d is DrawableStoryboard);

        public void ApplyToPlayer(Player player)
        {
            // Playfield position adjusting

            bool hasReplay = osuInputManager.ReplayInputHandler.IsNotNull();

            // If it's a replay we don't need to do mouse conversion
            if (hasReplay)
            {
                playfield.OnUpdate += _ => playfield.MoveTo(OsuPlayfield.BASE_SIZE / 2 - playfield.Cursor!.ActiveCursor.Position);
            }
            else
            {
                // Added this way, ExternalMousePosGetter receives OnMouseMove before the playfield drawables, so it can block propagation to the playfield
                ExternalMousePosGetter externalMousePosGetter;
                ruleset.PlayfieldAdjustmentContainer.Add(externalMousePosGetter = new ExternalMousePosGetter { RelativeSizeAxes = Axes.Both });

                // Reset playfield position while paused so the resume overlay reads the real cursor position correctly.
                // This avoids the resume overlay forcing the user to move the mouse to the center, which would cause a cursor jump/teleportation when resuming.
                ruleset.IsPaused.BindValueChanged(p =>
                {
                    if (p.NewValue) ruleset.Playfield.Position = Vector2.Zero;
                });

                playfield.OnUpdate += _ =>
                {
                    var mousePos = externalMousePosGetter.MousePos;

                    // We convert the mouse position to the coords of the cursor in playfield local space using the playfield parent because the playfield is moving so the values would be wrong
                    Vector2 osuPos = playfield.Parent!.ToLocalSpace(mousePos);

                    new ConvertedMousePositionAbsoluteInput { Position = playfield.ToScreenSpace(osuPos) }.Apply(osuInputManager.CurrentState, osuInputManager);
                    playfield.MoveTo(OsuPlayfield.BASE_SIZE / 2 - osuPos);
                };
            }

            // Background and storyboard position adjusting

            Storyboard storyboard = player.GameplayState.Storyboard;

            if (!CentredBackground.Value)
            {
                Action<Drawable> backgroundAction = null!;
                backgroundAction = _ => player.ApplyToBackground(bsb =>
                {
                    if (isExited(player)) // Background screen beatmap persists upon exiting the play, so manual event removal and its repositioning to (x, y) = (0, 0) is necessary
                    {
                        bsb.OnUpdate -= backgroundAction;
                        bsb.MoveTo(Vector2.Zero);

                        return;
                    }

                    bool storyboardReplacesBackground = storyboard.ReplacesBackground && storyboard.HasDrawable; // Based on Player's
                    if (!storyboardReplacesBackground || !showStoryboard.Value)
                        bsb.MoveTo(miscPos);
                });

                player.ApplyToBackground(bsb => bsb.OnUpdate += backgroundAction);
            }

            if (storyboard.HasDrawable && !CentredStoryboard.Value)
            {
                Drawable? drawableStoryboard = getDrawableStoryboard(player); // The drawable storyboard may still be loaded even if Show storyboard was just disabled while entering the play

                if (drawableStoryboard.IsNotNull())
                {
                    drawableStoryboard.OnUpdate += _ => drawableStoryboard.MoveTo(miscPos);

                    return;
                }

                showStoryboard.BindValueChanged(ss => Task.Run(async () => // Task.Run to not have 'async' lambda with delegate returning 'void'
                {
                    if (!ss.NewValue)
                        return;

                    showStoryboard.UnbindEvents(); // Show storyboard being enabled even briefly during a play means the drawable storyboard will load into memory if the play continues long enough

                    drawableStoryboard = getDrawableStoryboard(player);

                    while (drawableStoryboard.IsNull())
                    {
                        await Task.Delay(200).ConfigureAwait(false);
                        if (isExited(player))
                            return;

                        drawableStoryboard = getDrawableStoryboard(player);
                    }

                    drawableStoryboard.OnUpdate += _ => drawableStoryboard.MoveTo(miscPos);
                }), true);
            }
        }
    }
}
