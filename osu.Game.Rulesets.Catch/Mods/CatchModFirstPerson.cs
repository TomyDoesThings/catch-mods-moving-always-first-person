// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Linq;
using osu.Framework.Extensions.ObjectExtensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Localisation;
using osu.Game.Rulesets.Catch.Objects;
using osu.Game.Rulesets.Catch.UI;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.UI;
using osu.Game.Screens.Backgrounds;
using osu.Game.Screens.Play;
using osu.Game.Storyboards.Drawables;
using osuTK;

namespace osu.Game.Rulesets.Catch.Mods
{
    public class CatchModFirstPerson : ModFirstPerson<CatchHitObject>
    {
        public override LocalisableString Description => "Enter the catcher's perspective!";
        public override Type[] IncompatibleMods => base.IncompatibleMods.Concat(new[] { typeof(ModTouchDevice) }).ToArray();

        // osu!(standard) is two dimensions in note hitting, X and Y, so position adjustment logic is for Vector2 (X, Y)
        private Vector2 miscPos => Playfield.Position * PlayfieldMiscPosScale;

        private Catcher catcher = null!;

        public override void ApplyToDrawableRuleset(DrawableRuleset<CatchHitObject> drawableRuleset)
        {
            base.ApplyToDrawableRuleset(drawableRuleset);

            catcher = ((CatchPlayfield)Playfield).Catcher;
        }

        protected override void InitialisePlayfieldForFirstPerson(Playfield playfield) =>
            playfield.OnUpdate += _ => playfield.MoveToX(CatchPlayfield.CENTER_X - catcher.X);

        protected override void FirstPersonify(BackgroundScreenBeatmap backgroundScreenBeatmap) => backgroundScreenBeatmap.MoveToX(miscPos.X);
        protected override void FirstPersonify(DrawableStoryboard drawableStoryboard) => drawableStoryboard.MoveToX(miscPos.X);
        protected override void FirstPersonify(Container container) => container.MoveToX(miscPos.X);

        protected override PlayfieldAdjustmentContainer CreatePlayfieldAdjustmentContainer(DrawableRuleset drawableRuleset) =>
            ((DrawableCatchRuleset)drawableRuleset).CreatePlayfieldAdjustmentContainer();

        protected override void DefaultBackground(BackgroundScreenBeatmap backgroundScreenBeatmap) => backgroundScreenBeatmap.MoveToX(0.0f);

        public override void ApplyToHUD(HUDOverlay overlay)
        {
            base.ApplyToHUD(overlay);
            if (PlayfieldCentreAimReference.IsNull())
                return;

            // The | could possibly be playfield centered better, but the logic with scaling and different drawables does not make this easy

            PlayfieldCentreAimReference.OnUpdate += _ => FirstPersonify(PlayfieldCentreAimReference);
            overlay.Add(PlayfieldCentreAimReference);
        }
    }
}
