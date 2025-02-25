﻿using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading.Tasks;
using CoreAnimation;
using CoreGraphics;
using Foundation;
using Microsoft.Maui.Devices;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Media;
using ObjCRuntime;
using UIKit;
using static Microsoft.Maui.Primitives.Dimension;

namespace Microsoft.Maui.Platform
{
	public static partial class ViewExtensions
	{
		internal const string BackgroundLayerName = "MauiBackgroundLayer";

		public static void UpdateIsEnabled(this UIView platformView, IView view)
		{
			if (platformView is not UIControl uiControl)
				return;

			uiControl.Enabled = view.IsEnabled;
		}

		public static void Focus(this UIView platformView, FocusRequest request)
		{
			request.IsFocused = platformView.BecomeFirstResponder();
		}

		public static void Unfocus(this UIView platformView, IView view)
		{
			platformView.ResignFirstResponder();
		}

		public static void UpdateVisibility(this UIView platformView, IView view) =>
			ViewExtensions.UpdateVisibility(platformView, view.Visibility);

		public static void UpdateVisibility(this UIView platformView, Visibility visibility)
		{
			switch (visibility)
			{
				case Visibility.Visible:
					platformView.Inflate();
					platformView.Hidden = false;
					break;
				case Visibility.Hidden:
					platformView.Inflate();
					platformView.Hidden = true;
					break;
				case Visibility.Collapsed:
					platformView.Hidden = true;
					platformView.Collapse();
					break;
			}
		}

		public static void UpdateBackground(this ContentView platformView, IBorderStroke border)
		{
			bool hasShape = border.Shape != null;

			if (hasShape)
			{
				platformView.UpdateMauiCALayer(border);
			}
		}

		public static void UpdateBackground(this UIView platformView, IView view)
		{
			platformView.UpdateBackground(view.Background, view as IButtonStroke);
		}

		public static void UpdateBackground(this UIView platformView, Paint? paint, IButtonStroke? stroke = null)
		{
			// Remove previous background gradient layer if any
			platformView.RemoveBackgroundLayer();

			if (paint.IsNullOrEmpty())
			{
				if (platformView is LayoutView)
					platformView.BackgroundColor = null;
				else
					return;
			}


			if (paint is SolidPaint solidPaint)
			{
				Color backgroundColor = solidPaint.Color;

				if (backgroundColor == null)
					platformView.BackgroundColor = ColorExtensions.BackgroundColor;
				else
					platformView.BackgroundColor = backgroundColor.ToPlatform();

				return;
			}
			else if (paint is GradientPaint gradientPaint)
			{
				var backgroundLayer = gradientPaint?.ToCALayer(platformView.Bounds);

				if (backgroundLayer != null)
				{
					backgroundLayer.Name = BackgroundLayerName;
					platformView.BackgroundColor = UIColor.Clear;

					backgroundLayer.UpdateLayerBorder(stroke);

					platformView.InsertBackgroundLayer(backgroundLayer, 0);
				}
			}
		}

		public static void UpdateFlowDirection(this UIView platformView, IView view)
		{
			UISemanticContentAttribute updateValue = platformView.SemanticContentAttribute;

			switch (view.FlowDirection)
			{
				case FlowDirection.MatchParent:
					updateValue = GetParentMatchingSemanticContentAttribute(view);
					break;
				case FlowDirection.LeftToRight:
					updateValue = UISemanticContentAttribute.ForceLeftToRight;
					break;
				case FlowDirection.RightToLeft:
					updateValue = UISemanticContentAttribute.ForceRightToLeft;
					break;
			}

			if (updateValue != platformView.SemanticContentAttribute)
			{
				platformView.SemanticContentAttribute = updateValue;

				if (view is ITextAlignment)
				{
					// A change in flow direction may mean a change in text alignment
					view.Handler?.UpdateValue(nameof(ITextAlignment.HorizontalTextAlignment));
				}

				PropagateFlowDirection(updateValue, view);
			}
		}

		static UISemanticContentAttribute GetParentMatchingSemanticContentAttribute(IView view)
		{
			var parent = view.Parent?.Handler?.PlatformView as UIView;

			if (parent == null)
			{
				// No parent, no direction we need to match
				return UISemanticContentAttribute.Unspecified;
			}

			var parentSemanticContentAttribute = parent.SemanticContentAttribute;

			if (parentSemanticContentAttribute == UISemanticContentAttribute.ForceLeftToRight
				|| parentSemanticContentAttribute == UISemanticContentAttribute.ForceRightToLeft)
			{
				return parentSemanticContentAttribute;
			}

			// The parent view isn't using an explicit direction, so there's nothing for us to match
			return UISemanticContentAttribute.Unspecified;
		}

		static void PropagateFlowDirection(UISemanticContentAttribute semanticContentAttribute, IView view)
		{
			if (semanticContentAttribute != UISemanticContentAttribute.ForceLeftToRight
				&& semanticContentAttribute != UISemanticContentAttribute.ForceRightToLeft)
			{
				// If the current view isn't using an explicit LTR/RTL value, there's nothing to propagate
				return;
			}

			// If this view has any child/content views, we'll need to call UpdateFlowDirection on them
			// because they _may_ need to update their FlowDirection to match this view

			if (view is IContainer container)
			{
				foreach (var child in container)
				{
					if (child.Handler?.PlatformView is UIView uiView)
					{
						uiView.UpdateFlowDirection(child);
					}
				}
			}
			else if (view is IContentView contentView
				&& contentView.PresentedContent is IView child)
			{
				if (child.Handler?.PlatformView is UIView uiView)
				{
					uiView.UpdateFlowDirection(child);
				}
			}
		}

		public static void UpdateOpacity(this UIView platformView, IView view)
		{
			platformView.Alpha = (float)view.Opacity;
		}

		public static void UpdateAutomationId(this UIView platformView, IView view) =>
			platformView.AccessibilityIdentifier = view.AutomationId;

		public static void UpdateClip(this UIView platformView, IView view)
		{
			if (platformView is WrapperView wrapper)
				wrapper.Clip = view.Clip;
		}

		public static void UpdateShadow(this UIView platformView, IView view)
		{
			var shadow = view.Shadow;
			var clip = view.Clip;

			// If there is a clip shape, then the shadow should be applied to the clip layer, not the view layer
			if (clip == null)
			{
				if (shadow == null)
					platformView.ClearShadow();
				else
					platformView.SetShadow(shadow);
			}
			else
			{
				if (platformView is WrapperView wrapperView)
					wrapperView.Shadow = view.Shadow;
			}
		}
		public static void UpdateBorder(this UIView platformView, IView view)
		{
			var border = (view as IBorder)?.Border;
			if (platformView is WrapperView wrapperView)
				wrapperView.Border = border;
		}

		internal static T? FindDescendantView<T>(this UIView view, Func<T, bool> predicate) where T : UIView
		{
			var queue = new Queue<UIView>();
			queue.Enqueue(view);

			while (queue.Count > 0)
			{
				var descendantView = queue.Dequeue();

				if (descendantView is T result && predicate.Invoke(result))
					return result;

				for (var i = 0; i < descendantView.Subviews?.Length; i++)
					queue.Enqueue(descendantView.Subviews[i]);
			}

			return null;
		}

		public static T? FindDescendantView<T>(this UIView view) where T : UIView =>
			FindDescendantView<T>(view, (_) => true);

		public static void UpdateBackgroundLayerFrame(this UIView view)
		{
			if (view == null || view.Frame.IsEmpty)
				return;

			var layer = view.Layer;

			UpdateBackgroundLayerFrame(layer, view.Bounds);
		}

		static void UpdateBackgroundLayerFrame(CALayer layer, CGRect bounds)
		{
			if (layer == null || layer.Sublayers == null || layer.Sublayers.Length == 0)
				return;

			foreach (var sublayer in layer.Sublayers)
			{
				if (sublayer.Name == BackgroundLayerName && sublayer.Frame != bounds)
				{
					sublayer.Frame = bounds;
					break;
				}

				UpdateBackgroundLayerFrame(sublayer, bounds);
			}
		}

		public static void InvalidateMeasure(this UIView platformView, IView view)
		{
			platformView.SetNeedsLayout();
			platformView.Superview?.SetNeedsLayout();
		}

		public static void UpdateWidth(this UIView platformView, IView view)
		{
			UpdateFrame(platformView, view);
		}

		public static void UpdateHeight(this UIView platformView, IView view)
		{
			UpdateFrame(platformView, view);
		}

		public static void UpdateMinimumHeight(this UIView platformView, IView view)
		{
			UpdateFrame(platformView, view);
		}

		public static void UpdateMaximumHeight(this UIView platformView, IView view)
		{
			UpdateFrame(platformView, view);
		}

		public static void UpdateMinimumWidth(this UIView platformView, IView view)
		{
			UpdateFrame(platformView, view);
		}

		public static void UpdateMaximumWidth(this UIView platformView, IView view)
		{
			UpdateFrame(platformView, view);
		}

		public static void UpdateFrame(UIView platformView, IView view)
		{
			if (!IsExplicitSet(view.Width) || !IsExplicitSet(view.Height))
			{
				// Ignore the initial setting of the value; the initial layout will take care of it
				return;
			}

			// Updating the frame (assuming it's an actual change) will kick off a layout update
			// Handling of the default width/height will be taken care of by GetDesiredSize
			var currentFrame = platformView.Frame;
			platformView.Frame = new CoreGraphics.CGRect(currentFrame.X, currentFrame.Y, view.Width, view.Height);
		}

		public static async Task UpdateBackgroundImageSourceAsync(this UIView platformView, IImageSource? imageSource, IImageSourceServiceProvider? provider)
		{
			if (provider == null)
				return;

			if (imageSource != null)
			{
				var service = provider.GetRequiredImageSourceService(imageSource);
				var result = await service.GetImageAsync(imageSource);
				var backgroundImage = result?.Value;

				if (backgroundImage == null)
					return;

				platformView.BackgroundColor = UIColor.FromPatternImage(backgroundImage);
			}
		}

		public static int IndexOfSubview(this UIView platformView, UIView subview)
		{
			if (platformView.Subviews.Length == 0)
				return -1;

			return Array.IndexOf(platformView.Subviews, subview);
		}

		public static UIImage? ConvertToImage(this UIView view)
		{
			var imageRenderer = new UIGraphicsImageRenderer(view.Bounds.Size);

			return imageRenderer.CreateImage((a) =>
			{
				view.Layer.RenderInContext(a.CGContext);
			});
		}

		public static UINavigationController? GetNavigationController(this UIView view)
		{
			var rootController = view.Window?.RootViewController;
			if (rootController is UINavigationController nc)
				return nc;

			return rootController?.NavigationController;
		}

		internal static void Collapse(this UIView view)
		{
			// See if this view already has a collapse constraint we can use
			foreach (var constraint in view.Constraints)
			{
				if (constraint is CollapseConstraint collapseConstraint)
				{
					// Active the collapse constraint; that will squish the view down to zero height
					collapseConstraint.Active = true;
					return;
				}
			}

			// Set up a collapse constraint and turn it on
			var collapse = new CollapseConstraint();
			view.AddConstraint(collapse);
			collapse.Active = true;
		}

		internal static bool Inflate(this UIView view)
		{
			// Find and deactivate the collapse constraint, if any; the view will go back to its normal height
			foreach (var constraint in view.Constraints)
			{
				if (constraint is CollapseConstraint collapseConstraint)
				{
					collapseConstraint.Active = false;
					return true;
				}
			}

			return false;
		}

		public static void ClearSubviews(this UIView view)
		{
			for (int n = view.Subviews.Length - 1; n >= 0; n--)
			{
				view.Subviews[n].RemoveFromSuperview();
			}
		}

		internal static Rect GetPlatformViewBounds(this IView view)
		{
			var platformView = view?.ToPlatform();
			if (platformView == null)
			{
				return new Rect();
			}

			return platformView.GetPlatformViewBounds();
		}

		internal static Rect GetPlatformViewBounds(this UIView platformView)
		{
			if (platformView == null)
				return new Rect();

			var superview = platformView;
			while (superview.Superview is not null)
			{
				superview = superview.Superview;
			}

			var convertPoint = platformView.ConvertRectToView(platformView.Bounds, superview);

			var X = convertPoint.X;
			var Y = convertPoint.Y;
			var Width = convertPoint.Width;
			var Height = convertPoint.Height;

			return new Rect(X, Y, Width, Height);
		}

		internal static Matrix4x4 GetViewTransform(this IView view)
		{
			var platformView = view?.ToPlatform();
			if (platformView == null)
				return new Matrix4x4();
			return platformView.Layer.GetViewTransform();
		}

		internal static Matrix4x4 GetViewTransform(this UIView view)
			=> view.Layer.GetViewTransform();

		internal static Point GetLocationOnScreen(this UIView view) =>
			view.GetPlatformViewBounds().Location;

		internal static Point? GetLocationOnScreen(this IElement element)
		{
			if (element.Handler?.MauiContext == null)
				return null;

			return (element.ToPlatform())?.GetLocationOnScreen();
		}

		internal static Graphics.Rect GetBoundingBox(this IView view)
			=> view.ToPlatform().GetBoundingBox();

		internal static Graphics.Rect GetBoundingBox(this UIView? platformView)
		{
			if (platformView == null)
				return new Rect();
			var nvb = platformView.GetPlatformViewBounds();
			var transform = platformView.GetViewTransform();
			var radians = transform.ExtractAngleInRadians();
			var rotation = CoreGraphics.CGAffineTransform.MakeRotation((nfloat)radians);
			CGAffineTransform.CGRectApplyAffineTransform(nvb, rotation);
			return new Rect(nvb.X, nvb.Y, nvb.Width, nvb.Height);
		}

		internal static Rect GetFrameRelativeTo(this UIView view, UIView relativeTo)
		{
			var viewWindowLocation = view.GetLocationOnScreen();
			var relativeToLocation = relativeTo.GetLocationOnScreen();

			return
				new Rect(
						new Point(viewWindowLocation.X - relativeToLocation.X, viewWindowLocation.Y - relativeToLocation.Y),
						new Graphics.Size(view.Bounds.Width, view.Bounds.Height)
					);
		}

		internal static UIView? GetParent(this UIView? view)
		{
			return view?.Superview;
		}

		internal static Size LayoutToMeasuredSize(this IView view, double width, double height)
		{
			var size = view.Measure(width, height);
			var platformFrame = new CGRect(0, 0, size.Width, size.Height);

			if (view.Handler is IPlatformViewHandler viewHandler && viewHandler.PlatformView != null)
				viewHandler.PlatformView.Frame = platformFrame;

			view.Arrange(platformFrame.ToRectangle());
			return size;
		}

		public static void UpdateInputTransparent(this UIView platformView, IViewHandler handler, IView view)
		{
			if (view is ITextInput textInput)
			{
				platformView.UpdateInputTransparent(textInput.IsReadOnly, view.InputTransparent);
				return;
			}

			platformView.UserInteractionEnabled = !view.InputTransparent;
		}

		public static void UpdateInputTransparent(this UIView platformView, bool isReadOnly, bool inputTransparent)
		{
			platformView.UserInteractionEnabled = !(isReadOnly || inputTransparent);
		}


		internal static UIToolTipInteraction? GetToolTipInteraction(this UIView platformView)
		{
			UIToolTipInteraction? interaction = default;

			if (OperatingSystem.IsMacCatalystVersionAtLeast(15)
				|| OperatingSystem.IsIOSVersionAtLeast(15))
			{
				if (platformView is UIControl control)
				{
					interaction = control.ToolTipInteraction;
				}
				else
				{
					if (platformView.Interactions is not null)
					{
						foreach (var ia in platformView.Interactions)
						{
							if (ia is UIToolTipInteraction toolTipInteraction)
							{
								interaction = toolTipInteraction;
								break;
							}
						}
					}
				}
			}

			return interaction;
		}

		public static void UpdateToolTip(this UIView platformView, ToolTip? tooltip)
		{
			// UpdateToolTips were added in 15.0 for both iOS and MacCatalyst
			if (OperatingSystem.IsMacCatalystVersionAtLeast(15)
				|| OperatingSystem.IsIOSVersionAtLeast(15))
			{
				string? text = tooltip?.Content?.ToString();
				var interaction = platformView.GetToolTipInteraction();

				if (interaction is null)
				{
					if (!string.IsNullOrEmpty(text))
					{
						interaction = new UIToolTipInteraction(text);
						platformView.AddInteraction(interaction);
					}
				}
				else
				{
					interaction.DefaultToolTip = text;
				}
			}
		}

		internal static IWindow? GetHostedWindow(this IView? view)
			=> GetHostedWindow(view?.Handler?.PlatformView as UIView);

		internal static IWindow? GetHostedWindow(this UIView? view)
			=> GetHostedWindow(view?.Window);

		internal static bool IsLoaded(this UIView uiView)
		{
			if (uiView == null)
				return false;

			return uiView.Window != null;
		}

		internal static IDisposable OnLoaded(this UIView uiView, Action action)
		{
			if (uiView.IsLoaded())
			{
				action();
				return new ActionDisposable(() => { });
			}

			Dictionary<NSString, NSObject> observers = new Dictionary<NSString, NSObject>();
			ActionDisposable? disposable = null;
			disposable = new ActionDisposable(() =>
			{
				disposable = null;
				foreach (var observer in observers)
				{
					uiView.Layer.RemoveObserver(observer.Value, observer.Key);
					observers.Remove(observer.Key);
				}
			});

			// Ideally we could wire into UIView.MovedToWindow but there's no way to do that without just inheriting from every single
			// UIView. So we just make our best attempt by observering some properties that are going to fire once UIView is attached to a window.			
			observers.Add(new NSString("bounds"), (NSObject)uiView.Layer.AddObserver("bounds", Foundation.NSKeyValueObservingOptions.OldNew, (oc) => OnLoadedCheck(oc)));
			observers.Add(new NSString("frame"), (NSObject)uiView.Layer.AddObserver("frame", Foundation.NSKeyValueObservingOptions.OldNew, (oc) => OnLoadedCheck(oc)));

			// OnLoaded is called at the point in time where the xplat view knows it's going to be attached to the window.
			// So this just serves as a way to queue a call on the UI Thread to see if that's enough time for the window
			// to get attached.
			uiView.BeginInvokeOnMainThread(() => OnLoadedCheck(null));

			void OnLoadedCheck(NSObservedChange? nSObservedChange = null)
			{
				if (disposable != null)
				{
					if (uiView.IsLoaded())
					{
						disposable.Dispose();
						disposable = null;
						action();
					}
					else if (nSObservedChange != null)
					{
						// In some cases (FlyoutPage) the arrange and measure all take place before
						// the view is added to the screen so this queues up a second check that
						// hopefully will fire loaded once the view is added to the window.
						// None of this code is great but I haven't found a better way
						// for an outside observer to know when a subview is added to a window
						uiView.BeginInvokeOnMainThread(() => OnLoadedCheck(null));
					}
				}
			};

			return disposable;
		}

		internal static IDisposable OnUnloaded(this UIView uiView, Action action)
		{

			if (!uiView.IsLoaded())
			{
				action();
				return new ActionDisposable(() => { });
			}

			Dictionary<NSString, NSObject> observers = new Dictionary<NSString, NSObject>();
			ActionDisposable? disposable = null;
			disposable = new ActionDisposable(() =>
			{
				disposable = null;
				foreach (var observer in observers)
				{
					uiView.Layer.RemoveObserver(observer.Value, observer.Key);
					observers.Remove(observer.Key);
				}
			});

			// Ideally we could wire into UIView.MovedToWindow but there's no way to do that without just inheriting from every single
			// UIView. So we just make our best attempt by observering some properties that are going to fire once UIView is attached to a window.	
			observers.Add(new NSString("bounds"), (NSObject)uiView.Layer.AddObserver("bounds", Foundation.NSKeyValueObservingOptions.OldNew, (_) => UnLoadedCheck()));
			observers.Add(new NSString("frame"), (NSObject)uiView.Layer.AddObserver("frame", Foundation.NSKeyValueObservingOptions.OldNew, (_) => UnLoadedCheck()));

			// OnUnloaded is called at the point in time where the xplat view knows it's going to be detached from the window.
			// So this just serves as a way to queue a call on the UI Thread to see if that's enough time for the window
			// to get detached.
			uiView.BeginInvokeOnMainThread(UnLoadedCheck);

			void UnLoadedCheck()
			{
				if (!uiView.IsLoaded() && disposable != null)
				{
					disposable.Dispose();
					disposable = null;
					action();
				}
			};

			return disposable;
		}

		internal static void UpdateLayerBorder(this CoreAnimation.CALayer layer, IButtonStroke? stroke)
		{
			if (stroke == null)
				return;

			if (stroke.StrokeColor != null)
				layer.BorderColor = stroke.StrokeColor.ToCGColor();

			if (stroke.StrokeThickness >= 0)
				layer.BorderWidth = (float)stroke.StrokeThickness;

			if (stroke.CornerRadius >= 0)
				layer.CornerRadius = stroke.CornerRadius;
		}
	}
}
