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

        private readonly Dictionary<string, List<SnapshotPoint>> _navigateGroups = new Dictionary<string, List<SnapshotPoint>>();

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
                foreach (var group in _navigateGroups)
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

            var textViewLines = _wpfTextView.TextViewLines;
            //create point below caret
            var startPoint = _wpfTextView.Caret.Position.BufferPosition; //textViewLines.FirstVisibleLine.Start;
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

            //int groups_count = points.Count / dict_size;
            int groupSize = dictSize;
            int hotSpotsCount = hotSpots.Count;
            int singles = dictSize < hotSpotsCount ? dictSize : hotSpotsCount;
            int rest = hotSpotsCount - groupSize;

            if (rest > 0)
            {
                groupSize = hotSpotsCount / groupSize;
                singles -= groupSize;
            }

            int idx = 0;
            string key = NavigationDict[idx++];
            addNavigationForSingles(hotSpots);

                        
            foreach (var point in hotSpots)
            {
                _navigateMap[key] = point;
                addAdornmentPoint(key, point);
                //groups
                key = NavigationDict[idx];
                _navigateGroups[key] = new List<SnapshotPoint>(point);
                _navigateGroups[key].Add(point);
            }

            if (idx == 0)
            {
                _easyMotionUtil.ChangeToLookingCharNotFound();
            }
        }

        private void addNavigationForSingles(List<SnapshotPoint> points)
        {
            int dict_size = NavigationDict.Length;
            int groups_count = points.Count / dict_size;
            for (int i = 0; i < dict_size - groups_count; ++i)
            {
                _navigateMap[NavigationDict[i]] = points[i];
            }
        }
        private List<SnapshotPoint> findHotSpots(SnapshotPoint startPoint, SnapshotPoint endPoint)
        {
            //????
            var snapshot = startPoint.Snapshot;
            List<SnapshotPoint> points = new List<SnapshotPoint>();
            for (int i = startPoint.Position + 1; i < endPoint.Position; i++)
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
            //how-to navigate?             
            //var NavigateGroup = _navigateGroups.First(e => e.Key == key);
            //if (NavigateGroup.Value != null)
            //{
            //    _adornmentLayer.RemoveAdornmentsByTag(_tag);
            //    AddAdornmentsRegion(NavigateGroup.Value.First(), NavigateGroup.Value.Last());
            //    //_easyMotionUtil.ChangeToLookingForChar();
            //    return false;
            //}


            SnapshotPoint point;
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

        private void addAdornmentPoint(string key, SnapshotPoint point)
        {
            var hotSpotUI = CreateHotSpotUI(point, key);
            if (hotSpotUI == null) return;
            _adornmentLayer.AddAdornment(new SnapshotSpan(point, 1), _tag, hotSpotUI);
        }

        private TextBox CreateHotSpotUI(SnapshotPoint point, string key)
        {
            TextBounds bounds;
            //catch if user scroll layout so that one point going out of screen.
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
