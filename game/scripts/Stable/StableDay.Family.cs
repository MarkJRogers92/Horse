using System;
using System.Collections.Generic;
using System.Linq;
using Cloverbrook.Domain;
using Cloverbrook.Game.UI;
using Godot;

namespace Cloverbrook.Game.Stable;

/// <summary>
/// D-034 family tree. Three generations from the domain <see cref="FamilyTree"/>: the horse on the left, its sire and dam,
/// and four grandparents on the right, as portrait cards joined by simple lines. Click a relative to open their tree.
/// </summary>
public partial class StableDay
{
    private const int FamilyCardW = 270, FamilyCardH = 104, FamilyGap = 8, FamilyColumnGap = 64;

    private Control _familyOverlay = null!;
    private Label _familyTitle = null!;
    private Control _familyCanvas = null!;
    private Label _familyStory = null!;
    private Button _familyBack = null!;
    private readonly Stack<HorseId> _familyHistory = new();
    private HorseId? _familyShown;

    private void BuildFamilyOverlay(Control root)
    {
        _familyOverlay = CozyTheme.Dialog(root, "Family", 1000, out var body, out var buttons, out _familyTitle);
        _familyCanvas = new Control { SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter };
        var height = 4 * FamilyCardH + 3 * FamilyGap;
        _familyCanvas.CustomMinimumSize = new Vector2(3 * FamilyCardW + 2 * FamilyColumnGap, height);
        body.AddChild(_familyCanvas);
        _familyStory = SmallLabel("", 15, false);
        _familyStory.HorizontalAlignment = HorizontalAlignment.Center;
        body.AddChild(_familyStory);
        _familyBack = new Button { Text = "Back", CustomMinimumSize = new Vector2(110, 46) };
        _familyBack.Pressed += () => { if (_familyHistory.Count > 0) ShowFamily(_familyHistory.Pop(), push: false); };
        buttons.AddChild(_familyBack);
        var close = new Button { Text = "Close", CustomMinimumSize = new Vector2(110, 46) };
        close.Pressed += () => _familyOverlay.Visible = false;
        buttons.AddChild(close);
        CozyTheme.FinishDialog(body, buttons);
    }

    private void OpenFamily(HorseId horseId)
    {
        _familyHistory.Clear(); _familyShown = null;
        ShowFamily(horseId, push: false);
    }

    private void ShowFamily(HorseId horseId, bool push = true)
    {
        if (_state == null) return;
        try
        {
            var tree = FamilyTree.For(_state, horseId);
            if (push && _familyShown != null) _familyHistory.Push(_familyShown);
            _familyShown = horseId;
            _familyTitle.Text = $"{tree.Subject.Name}'s family";
            foreach (var old in _familyCanvas.GetChildren()) old.QueueFree();
            // Column x positions: subject, parents, grandparents. Rows are four grandparent slots tall.
            int colX(int c) => c * (FamilyCardW + FamilyColumnGap);
            int rowY(int r) => r * (FamilyCardH + FamilyGap);
            var centre = FamilyCardH / 2;
            var grand = tree.Grandparents;
            var gpY = Enumerable.Range(0, 4).Select(rowY).ToArray();
            var parentY = new[] { (gpY[0] + gpY[1]) / 2, (gpY[2] + gpY[3]) / 2 };
            var subjectY = (parentY[0] + parentY[1]) / 2;
            // Lines first so cards sit on top of them.
            void Link(int childCol, int childY, int parentColumn, int parentTop, bool known)
            {
                var line = new Line2D { Width = known ? 3 : 2, DefaultColor = known ? CozyTheme.AccentDeep : new Color(CozyTheme.Border, 0.9f), Antialiased = true, JointMode = Line2D.LineJointMode.Round };
                var from = new Vector2(colX(childCol) + FamilyCardW, childY + centre);
                var to = new Vector2(colX(parentColumn), parentTop + centre);
                var mid = from.X + FamilyColumnGap / 2f;
                line.Points = new[] { from, new Vector2(mid, from.Y), new Vector2(mid, to.Y), to };
                _familyCanvas.AddChild(line);
            }
            Link(0, subjectY, 1, parentY[0], tree.Sire != null); Link(0, subjectY, 1, parentY[1], tree.Dam != null);
            Link(1, parentY[0], 2, gpY[0], grand[0] != null); Link(1, parentY[0], 2, gpY[1], grand[1] != null);
            Link(1, parentY[1], 2, gpY[2], grand[2] != null); Link(1, parentY[1], 2, gpY[3], grand[3] != null);
            AddFamilyCard(tree.Subject, "This horse", colX(0), subjectY, subject: true);
            AddFamilyCard(tree.Sire, "Sire", colX(1), parentY[0]); AddFamilyCard(tree.Dam, "Dam", colX(1), parentY[1]);
            var captions = new[] { "Sire's sire", "Sire's dam", "Dam's sire", "Dam's dam" };
            for (var i = 0; i < 4; i++) AddFamilyCard(grand[i], captions[i], colX(2), gpY[i]);
            var story = tree.Story;
            if (story == null && tree.Subject.IsFounder) story = $"{tree.Subject.Name} is a founding horse: its coat story begins here.";
            _familyStory.Text = story == null ? "" : "Coat story: " + story;
            _familyStory.Visible = story != null;
            _familyBack.Visible = _familyHistory.Count > 0;
            _familyOverlay.Visible = true;
        }
        catch (Exception ex)
        {
            SetStatus("The family tree could not be shown. " + PlainError(ex));
        }
    }

    private void AddFamilyCard(FamilyNode? node, string caption, int x, int y, bool subject = false)
    {
        var card = new PanelContainer { Position = new Vector2(x, y), CustomMinimumSize = new Vector2(FamilyCardW, FamilyCardH), Size = new Vector2(FamilyCardW, FamilyCardH) };
        var style = CozyTheme.Box(node == null ? new Color(CozyTheme.Blush, 0.55f) : subject ? Colors.White : CozyTheme.Cream, 18, 8);
        style.BorderColor = node == null ? new Color(CozyTheme.Border, 0.8f) : subject ? CozyTheme.AccentDeep : CozyTheme.Border;
        style.SetBorderWidthAll(subject ? 4 : 2);
        card.AddThemeStyleboxOverride("panel", style);
        _familyCanvas.AddChild(card);
        var row = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore }; row.AddThemeConstantOverride("separation", 10); card.AddChild(row);
        if (node == null)
        {
            var mark = new Label { Text = "?", HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, CustomMinimumSize = new Vector2(72, 72), MouseFilter = Control.MouseFilterEnum.Ignore };
            mark.AddThemeFontSizeOverride("font_size", 42); mark.AddThemeColorOverride("font_color", new Color(CozyTheme.SoftText, 0.55f));
            row.AddChild(mark);
            var text = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, Alignment = BoxContainer.AlignmentMode.Center, MouseFilter = Control.MouseFilterEnum.Ignore };
            text.AddChild(SmallLabel(caption, 11)); text.AddChild(SmallLabel("Unknown", 15));
            row.AddChild(text);
            return;
        }
        var horse = _state!.Horses.Single(h => h.HorseId.Equals(node.HorseId));
        var portrait = Portrait(horse, 72, CardTint(horse)); portrait.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter; row.AddChild(portrait);
        var info = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, Alignment = BoxContainer.AlignmentMode.Center, MouseFilter = Control.MouseFilterEnum.Ignore };
        info.AddThemeConstantOverride("separation", 1);
        var name = new Label { Text = node.Name, ClipText = true, TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis, MouseFilter = Control.MouseFilterEnum.Ignore };
        name.AddThemeFontSizeOverride("font_size", 16); name.AddThemeFontOverride("font", CozyTheme.BoldFont);
        var tags = new List<string> { caption };
        if (node.IsFounder) tags.Add("Founding horse");
        if (node.IsSold) tags.Add("Sold");
        info.AddChild(name); info.AddChild(SmallLabel(string.Join(" · ", tags), 11));
        var coat = SmallLabel(node.VisibleCoat, 12, false); coat.MaxLinesVisible = 2; coat.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        info.AddChild(coat);
        info.AddChild(CozyTheme.HeartRating(node.Hearts, 14));
        row.AddChild(info);
        if (node.IsSold) card.Modulate = new Color(1, 1, 1, 0.85f);
        if (subject) return;
        card.TooltipText = $"Open {node.Name}'s family";
        card.MouseDefaultCursorShape = Control.CursorShape.PointingHand;
        var target = node.HorseId;
        card.GuiInput += e =>
        {
            if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left }) ShowFamily(target);
        };
    }
}
