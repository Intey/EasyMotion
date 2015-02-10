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
using Microsoft.VisualStudio.Text.Formatting;
using System.Windows;

namespace EasyMotion.Implementation.Adornment
{
    internal sealed class EasyMotionAdornmentController : IEasyMotionNavigator
    {
        private static readonly string[] NavigationDict =
            "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ"
            .Select(x => x.ToString())
            .ToArray();

        private readonly IEasyMotionUtil _easyMotionUtil;
        private readonly IWpfTextView _wpfTextView;
        private readonly IEditorFormatMap _editorFormatMap;
        private readonly IClassificationFormatMap _classificationFormatMap;
        private readonly Dictionary<string, SnapshotPoint> _navigateMap = new Dictionary<string, SnapshotPoint>();

        private readonly Dictionary<string, List<SnapshotPoint>> navigator = new Dictionary<string, List<SnapshotPoint>>();

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
            _adornmentLayer.RemoveAdornmentsByTag(_tag);
            try
            {
                foreach (var keypoint in _navigateMap)
                    addAdornmentPoint(keypoint.Key, keypoint.Value);
                foreach (var group in navigator)
                    foreach (var point in group.Value)
                        addAdornmentPoint(group.Key, point);
            }
            catch {/* nop */}
        }

        //creats view blocks on each founded key
        private void AddAdornmentsForPage()
        {
            Debug.Assert(_easyMotionUtil.State == EasyMotionState.LookingForDecision);


            if (_wpfTextView.InLayout) { return; }
            _navigateMap.Clear();
            navigator.Clear();

            var textViewLines = _wpfTextView.TextViewLines;
            //create point below caret
            //FIXME: if caret out of bounds - use first line.
            //get point after caret
            var snapshot = _wpfTextView.TextSnapshot;
            var startPoint = new SnapshotPoint(snapshot, _wpfTextView.Caret.Position.BufferPosition.Position + 1);
            var endPoint = textViewLines.LastVisibleLine.End;

            AddAdornmentsRegion(startPoint, endPoint);

        }

        private void AddAdornmentsRegion(SnapshotPoint startPoint, SnapshotPoint endPoint)
        {
            /* CONCEPT:
             * Creating places(hotspot) for step with hotkey depends on dictionary. 
             * When count of points(that needs to be marked with hotspot) greater
             * then dictionary length, should use grouping.
             */

            int dictSize = NavigationDict.Length;
            //find points
            List<SnapshotPoint> hotSpots = findHotSpots(startPoint, endPoint);
            int hotSpotsCount = hotSpots.Count;

            if (hotSpotsCount == 0)
            {
                _easyMotionUtil.ChangeToLookingCharNotFound();
                return;
            }

            int groupSize = dictSize;
            int dict_size = NavigationDict.Length;
            int groups_count = hotSpotsCount / dict_size
                + (hotSpotsCount % dict_size > 0 ? 1 : 0);
            int singles = dictSize < hotSpotsCount ? dictSize - groups_count : hotSpotsCount;

            int key_idx = 0;

            for (int i = 0; i < hotSpotsCount; ++i)
            {
                string key = NavigationDict[key_idx];
                addAdornmentPoint(key, hotSpots[i]);//create view
                addToNavigator(key, hotSpots[i]);//create link
                //increasing index if i < singles, otherwise - on each group change
                if (i < singles)
                    key_idx++;
                else if (i % groupSize == 0)
                    key_idx++;
            }

        }

        private void addToNavigator(string k, SnapshotPoint p)
        {
            if (navigator.ContainsKey(k))
                navigator[k].Add(p);
            else
            {
                navigator[k] = new List<SnapshotPoint>();
                navigator[k].Add(p);
            }
        }

        private List<SnapshotPoint> findHotSpots(SnapshotPoint startPoint, SnapshotPoint endPoint)
        {
            //????
            var snapshot = startPoint.Snapshot;
            List<SnapshotPoint> points = new List<SnapshotPoint>();
            for (int i = startPoint.Position; i <= endPoint.Position; i++)
            {
                var point = new SnapshotPoint(snapshot, i);
                if (isCharMatch(point /*, caseSens? */))
                {
                    points.Add(point);
                }
            }
            return points;
        }

        public bool NavigateTo(string key)
        {
            List<SnapshotPoint> points;
            SnapshotPoint point;
            //use First() because navigator may not contains searching key. 
            //When groups not realized, used TryGetValue() on Dictionary<string,SnapshotPoint>
            //FIXED[UNEXIST-GROUP-CHAR]: when call unexist char of grouped - can't get first
            var maybeGroup = navigator.FirstOrDefault(e => e.Key == key);
            if (maybeGroup.Value == null)
            {
                _easyMotionUtil.ChangeToLookingCharNotFound();
                return false;
            }
            //if it have many points(user want go to the group) refresh layer,
            //refill points with new keys
            if (maybeGroup.Value.Count > 1)
            {
                navigator.Clear();
                clearAdornmentLayer();
                AddAdornmentsRegion(maybeGroup.Value.First(), maybeGroup.Value.Last());
                //we are not navigated.
                return false;
            }
            else
            {
                navigator.TryGetValue(key, out points);
                if (points.Count != 1) return false;
                point = points[0];
                //ISIT: for case, when hotspot out of screen(scrolled) ?
                if (point.Snapshot != _wpfTextView.TextSnapshot)
                {
                    return false;
                }
                _wpfTextView.Caret.MoveTo(point);
                return true;
            }
        }

        private void addAdornmentPoint(string key, SnapshotPoint point)
        {
            var hotSpotUI = CreateHotSpotUI(point, key);
            if (hotSpotUI == null)
                return;
            _adornmentLayer.AddAdornment(new SnapshotSpan(point, 1), _tag, hotSpotUI);
        }

        private TextBox CreateHotSpotUI(SnapshotPoint point, string key)
        {
            TextBounds bounds;
            //BUG: when we invoke EasyMotion, it go to state LookingForChar, then we scroll down (caret out of bounds)
            //then press char - it crash. 
            bounds = _wpfTextView.TextViewLines.GetCharacterBounds(point);

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
        private bool isCharMatch(SnapshotPoint point, bool caseSens = false)
        {
            if (caseSens) return point.GetChar() == _easyMotionUtil.TargetChar;
            else return Char.ToLower(point.GetChar()) == Char.ToLower(_easyMotionUtil.TargetChar);
        }

    }
}
