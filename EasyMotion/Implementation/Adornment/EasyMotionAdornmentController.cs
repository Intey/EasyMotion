using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Classification;
using Microsoft.VisualStudio.Text.Editor;
using System.Windows;

namespace EasyMotion.Implementation.Adornment
{
    internal sealed class EasyMotionAdornmentController : IEasyMotionNavigator
    {
        private static readonly string[] NavigationKeys =
            "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ"
            .Select(x => x.ToString())
            .ToArray();

        private readonly IEasyMotionUtil _easyMotionUtil;
        private readonly IWpfTextView _wpfTextView;
        private readonly IEditorFormatMap _editorFormatMap;
        private readonly IClassificationFormatMap _classificationFormatMap;
        private readonly Dictionary<string, SnapshotPoint> _navigateMap = new Dictionary<string, SnapshotPoint>();
        private readonly object _tag = new object();
        private IAdornmentLayer _adornmentLayer;

        internal EasyMotionAdornmentController(IEasyMotionUtil easyMotionUtil, IWpfTextView wpfTextview, IEditorFormatMap editorFormatMap, IClassificationFormatMap classificationFormatMap)
        {
            _easyMotionUtil = easyMotionUtil;
            _wpfTextView = wpfTextview;
            _editorFormatMap = editorFormatMap;
            _classificationFormatMap = classificationFormatMap;
        }

        internal void SetAdornmentLayer(IAdornmentLayer adornmentLayer)
        {
            Debug.Assert(_adornmentLayer == null);
            _adornmentLayer = adornmentLayer;
            Subscribe();
        }

        private void Subscribe()
        {
            _easyMotionUtil.StateChanged += OnStateChanged;
            _wpfTextView.LayoutChanged += OnLayoutChanged;
        }

        private void Unsubscribe()
        {
            _easyMotionUtil.StateChanged -= OnStateChanged;
            _wpfTextView.LayoutChanged -= OnLayoutChanged;
        }

        private void OnStateChanged(object sender, EventArgs e)
        {
            if (_easyMotionUtil.State == EasyMotionState.LookingForDecision)

                AddAdornmentsForPage();
            else 
                clearAdornmentLayer();
        }

        private void clearAdornmentLayer()
        {
            _adornmentLayer.RemoveAdornmentsByTag(_tag);
        }

        private void OnLayoutChanged(object sender, EventArgs e)
        {
            switch (_easyMotionUtil.State)
            {
                case EasyMotionState.LookingCharNotFound:
                    _easyMotionUtil.ChangeToLookingForDecision(_easyMotionUtil.TargetChar);
                    break;

                case EasyMotionState.LookingForDecision:
                    ResetAdornments();
                    break;
            }
        }

        private void ResetAdornments()
        {
            var adorn = _adornmentLayer.Elements.Where(x => x.Tag == _tag);
            _adornmentLayer.RemoveAdornmentsByTag(_tag);

            foreach (var np in _navigateMap)
            {
                try
                {
                    //catch if user scroll layout so that one point going out of screen.
                    var bounds = _wpfTextView.TextViewLines.GetCharacterBounds(np.Value); //error appear there.
                    var hotSpotUI = CreateHotSpotUI(np);
                    _adornmentLayer.AddAdornment(new SnapshotSpan(np.Value, 1), _tag, hotSpotUI);
                }
                catch { /* no action */ }
            }
        }

        private void AddAdornments()
        {
            Debug.Assert(_easyMotionUtil.State == EasyMotionState.LookingForDecision);

            if (_wpfTextView.InLayout)
            {
                return;
            }

            _navigateMap.Clear();
            var textViewLines = _wpfTextView.TextViewLines;
            var startPoint = textViewLines.FirstVisibleLine.Start;
            var endPoint = textViewLines.LastVisibleLine.End;
            var snapshot = startPoint.Snapshot;
            int navigateIndex = 0;

            int ss_count = startPoint.Position - endPoint.Position;
            if (ss_count > NavigationKeys.Length)
            {
                //TODO: create groups
            }
            /* CONCEPT:
             * Creating places(hotspot) for step with hotkey depends on dictionary. 
             * When count of points(that needs to be marked with hotspot) greater
             * then dictionary length, should use grouping.
             */
            for (int i = startPoint.Position; i < endPoint.Position; i++)
            {
                var point = new SnapshotPoint(snapshot, i);

                if (Char.ToLower(point.GetChar()) == Char.ToLower(_easyMotionUtil.TargetChar) && navigateIndex < NavigationKeys.Length)
                {
                    string key = NavigationKeys[navigateIndex];
                    navigateIndex++;
                    AddNavigateToPoint(textViewLines, point, key);
                }
            }

            if (navigateIndex == 0)
            {
                _easyMotionUtil.ChangeToLookingCharNotFound();
            }
        }

        private void AddNavigateToPoint(IWpfTextViewLineCollection textViewLines, SnapshotPoint point, string key)
        {
            _navigateMap[key] = point;

            //_newNavigateMap[point] = key;

            var span = new SnapshotSpan(point, 1);
            var hotSpotUI = CreateHotSpotUI(point, key);

            _adornmentLayer.AddAdornment(span, _tag, hotSpotUI);
        }

        private TextBox CreateHotSpotUI(SnapshotPoint point, string key)
        {
            var bounds = _wpfTextView.TextViewLines.GetCharacterBounds(point);
            var textBox = new TextBox();
            textBox.Text = key;
            textBox.FontFamily = _classificationFormatMap.DefaultTextProperties.Typeface.FontFamily;
            textBox.Foreground = _editorFormatMap.GetProperties(EasyMotionNavigateFormatDefinition.Name).
                GetForegroundBrush(EasyMotionNavigateFormatDefinition.DefaultForegroundBrush);
            textBox.Background = _editorFormatMap.GetProperties(EasyMotionNavigateFormatDefinition.Name).
                GetBackgroundBrush(EasyMotionNavigateFormatDefinition.DefaultBackgroundBrush);

            textBox.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Canvas.SetTop(textBox, bounds.TextTop);
            Canvas.SetLeft(textBox, bounds.Left);
            Canvas.SetZIndex(textBox, 10);
            return textBox;
        }
        private TextBox CreateHotSpotUI(KeyValuePair<string, SnapshotPoint> hotspot)
        {
            return CreateHotSpotUI(hotspot.Value, hotspot.Key);
        }

        public bool NavigateTo(string key)
        {
            SnapshotPoint point;

            var NavigateGroup = _newNavigateMap.Where(e => e.Value == key);
            if (NavigateGroup.Count() > 1)
            {
                _adornmentLayer.RemoveAdornmentsByTag(_tag);
                //ResetAdornments();
                //AddAdornments(NavigateGroup.First().Key, NavigateGroup.Last().Key);
                //_easyMotionUtil.ChangeToLookingForChar();
            }

            if (!_navigateMap.TryGetValue(key, out point))
            {
                return false;
            }

            if (point.Snapshot != _wpfTextView.TextSnapshot)
            {
                return false;
            }

            _wpfTextView.Caret.MoveTo(point);
            return true;
        }
    }
}
