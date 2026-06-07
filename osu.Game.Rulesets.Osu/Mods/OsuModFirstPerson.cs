// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Linq;
using osu.Framework.Extensions.ObjectExtensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Input;
using osu.Framework.Input.Events;
using osu.Framework.Input.StateChanges;
using osu.Framework.Localisation;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Osu.Objects;
using osu.Game.Rulesets.Osu.UI;
using osu.Game.Rulesets.UI;
using osu.Game.Screens.Backgrounds;
using osu.Game.Screens.Play;
using osu.Game.Storyboards.Drawables;
using osuTK;

namespace osu.Game.Rulesets.Osu.Mods
{
    public partial class OsuModFirstPerson : ModFirstPerson<OsuHitObject>
    {
        public override LocalisableString Description => "Enter the cursor's perspective!";
        public override Type[] IncompatibleMods => base.IncompatibleMods.Concat(new[] { typeof(OsuModAutopilot), typeof(ModFlashlight), typeof(ModTouchDevice) }).ToArray();

        // osu!(standard) is two dimensions in note hitting, X and Y, so position adjustment logic is for Vector2 (X, Y)
        private Vector2 miscPos => Playfield.Position * PlayfieldMiscPosScale;

        protected override void InitialisePlayfieldForFirstPerson(Playfield playfield)
        {
            OsuInputManager osuInputManager = ((DrawableOsuRuleset)Ruleset).KeyBindingInputManager;

            bool hasReplay = osuInputManager.ReplayInputHandler.IsNotNull();

            // If it's a replay we don't need to do mouse conversion
            if (hasReplay)
            {
                playfield.OnUpdate += _ => playfield.MoveTo(OsuPlayfield.BASE_SIZE / 2.0f - playfield.Cursor!.ActiveCursor.Position);
            }
            else
            {
                // Added this way, ExternalMousePosGetter receives OnMouseMove before the playfield drawables, so it can block propagation to the playfield
                ExternalMousePosGetter externalMousePosGetter = new ExternalMousePosGetter { RelativeSizeAxes = Axes.Both };
                Ruleset.PlayfieldAdjustmentContainer.Add(externalMousePosGetter);

                // Reset playfield position while paused so the resume overlay reads the real cursor position correctly.
                // This avoids the resume overlay forcing the user to move the mouse to the center, which would cause a cursor jump/teleportation when resuming.
                /*ruleset.IsPaused.BindValueChanged(p =>
                {
                    if (p.NewValue) ruleset.Playfield.Position = Vector2.Zero;
                });*/

                playfield.OnUpdate += _ =>
                {
                    Vector2 mousePos = externalMousePosGetter.MousePos;

                    // We convert the mouse position to the coords of the cursor in playfield local space using the playfield parent because the playfield is moving so the values would be wrong
                    Vector2 osuPos = Playfield.Parent!.ToLocalSpace(mousePos);

                    new ConvertedMousePositionAbsoluteInput { Position = playfield.ToScreenSpace(osuPos) }.Apply(osuInputManager.CurrentState, osuInputManager);
                    playfield.MoveTo(OsuPlayfield.BASE_SIZE / 2.0f - osuPos);
                };
            }
        }

        protected override void FirstPersonify(BackgroundScreenBeatmap backgroundScreenBeatmap) => backgroundScreenBeatmap.MoveTo(miscPos);
        protected override void FirstPersonify(DrawableStoryboard drawableStoryboard) => drawableStoryboard.MoveTo(miscPos);
        protected override void FirstPersonify(Container container) => container.MoveTo(miscPos);

        protected override PlayfieldAdjustmentContainer CreatePlayfieldAdjustmentContainer(DrawableRuleset drawableRuleset) =>
            ((DrawableOsuRuleset)drawableRuleset).CreatePlayfieldAdjustmentContainer();

        protected override void DefaultBackground(BackgroundScreenBeatmap backgroundScreenBeatmap) => backgroundScreenBeatmap.MoveTo(Vector2.Zero);

        private partial class ConvertedMousePositionAbsoluteInput : MousePositionAbsoluteInput;

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

        public override void ApplyToHUD(HUDOverlay overlay)
        {
            base.ApplyToHUD(overlay);
            if (PlayfieldCentreAimReference.IsNull())
                return;

            // X-Axis
            PlayfieldCentreAimReference.Add(
                new Box { X = -CENTRE_AIM_ARM_DISTANCE, Width = CENTRE_AIM_BOX_LONGER_SIDE_LENGTH, Height = CENTRE_AIM_BOX_SHORTER_SIDE_LENGTH, Anchor = Anchor.Centre, Origin = Anchor.Centre });
            PlayfieldCentreAimReference.Add(
                new Box { X = CENTRE_AIM_ARM_DISTANCE, Width = CENTRE_AIM_BOX_LONGER_SIDE_LENGTH, Height = CENTRE_AIM_BOX_SHORTER_SIDE_LENGTH, Anchor = Anchor.Centre, Origin = Anchor.Centre });

            PlayfieldCentreAimReference.OnUpdate += _ => FirstPersonify(PlayfieldCentreAimReference);
            overlay.Add(PlayfieldCentreAimReference);
        }
    }
}
