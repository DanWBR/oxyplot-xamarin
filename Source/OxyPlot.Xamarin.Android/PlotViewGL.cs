// --------------------------------------------------------------------------------------------------------------------
// <copyright file="PlotView.cs" company="OxyPlot">
//   Copyright (c) 2014 OxyPlot contributors
// </copyright>
// <summary>
//   Represents a view that can show a <see cref="PlotModel" />.
// </summary>
// --------------------------------------------------------------------------------------------------------------------

namespace OxyPlot.Xamarin.Android
{
    using System;

    using global::Android.Content;
    using global::Android.Graphics;
    using global::Android.Util;
    using global::Android.Views;

    using OxyPlot;
    using SkiaSharp;
    using global::Android.Opengl;
    using Javax.Microedition.Khronos.Egl;
    using Javax.Microedition.Khronos.Opengles;

    public class PlotViewRenderer : Java.Lang.Object, GLSurfaceView.IRenderer
    {
        /// <summary>
        /// Plot view we are rendering to
        /// </summary>
        private PlotViewGL plotView;

        /// <summary>
        /// The current render context.
        /// </summary>
        private SKCanvasRenderContext rc;

        /// <summary>
        /// Context for rendering
        /// </summary>
        private GRContext context;

        /// <summary>
        /// Projection matrix
        /// </summary>
        private float[] projMatrix = new float[16];

        /// <summary>
        /// Initializes a new instance of the <see cref="PlotViewRenderer" /> class.
        /// </summary>
        /// <param name="plotView">The plotview which are rendering a surface for.</param>
        public PlotViewRenderer(PlotViewGL plotView)
        {
            this.plotView = plotView;
        }

        /// <summary>
        /// Triggered when an new frame is required to be drawn
        /// </summary>
        /// <param name="gl">opengl interface</param>
        public void OnDrawFrame(IGL10 gl)
        {
            var actualModel = plotView.ActualModel;
            if (actualModel == null)
            {
                return;
            }

            lock (plotView.InvalidateLock)
            {
                if (plotView.IsModelInvalidated)
                {
                    ((IPlotModel)actualModel).Update(plotView.UpdateDataFlag);
                    plotView.UpdateDataFlag = false;
                    plotView.IsModelInvalidated = false;
                }
            }

            var sampleCount = this.context.GetRecommendedSampleCount(GRPixelConfig.Rgba8888, 96.0f);

            var desc = new GRBackendRenderTargetDesc
            {
                Width = plotView.Width,
                Height = plotView.Height,
                Config = GRPixelConfig.Rgba8888,
                Origin = GRSurfaceOrigin.TopLeft,
                SampleCount = sampleCount,
                StencilBits = 0,
                RenderTargetHandle = IntPtr.Zero,
            };

            using (var surface = SKSurface.Create(this.context, desc))
            {
                var skcanvas = surface.Canvas;

                if (this.rc == null)
                {
                    this.rc = new SKCanvasRenderContext(PlotView.Scale);
                }

                this.rc.SetTarget(skcanvas);

                if (actualModel.Background.IsVisible())
                {
                    skcanvas.Clear(actualModel.Background.ToSKColor());
                }
                else
                {
                    skcanvas.Clear();
                }

                ((IPlotModel)actualModel).Render(this.rc, plotView.Width / PlotView.Scale, plotView.Height / PlotView.Scale);

                skcanvas.Flush();
            }
        }

        /// <summary>
        /// Triggered when the surface needs to be resized to fit the view
        /// </summary>
        /// <param name="gl">opengl interface</param>
        /// <param name="width">new surface width</param>
        /// <param name="height">new surface height</param>
        public void OnSurfaceChanged(IGL10 gl, int width, int height)
        {
            GLES20.GlViewport(0, 0, width, height);
        }

        /// <summary>
        /// Triggered when the surface is created
        /// </summary>
        /// <param name="gl">opengl interface</param>
        /// <param name="config">info about the opengl interface</param>
        public void OnSurfaceCreated(IGL10 gl, EGLConfig config)
        {
            this.context = GRContext.Create(GRBackend.OpenGL);
        }
    }

    /// <summary>
    /// Represents a view that can show a <see cref="PlotModel" />.
    /// </summary>
    public class PlotViewGL : GLSurfaceView, IPlotView
    {
        /// <summary>
        /// The factor that scales from OxyPlot´s device independent pixels (96 dpi) to 
        /// Android´s density-independent pixels (160 dpi).
        /// </summary>
        /// <remarks>See <a href="http://developer.android.com/guide/practices/screens_support.html">Supporting multiple screens.</a>.</remarks>
        public const double Scale = 160d / 96d;

        /// <summary>
        /// The invalidation lock object.
        /// </summary>
        private readonly object invalidateLock = new object();

        /// <summary>
        /// The touch points of the previous touch event.
        /// </summary>
        private ScreenPoint[] previousTouchPoints;

        /// <summary>
        /// The current model.
        /// </summary>
        private PlotModel model;

        /// <summary>
        /// The default controller
        /// </summary>
        private IPlotController defaultController;

        /// <summary>
        /// The model invalidated flag.
        /// </summary>
        private bool isModelInvalidated;

        /// <summary>
        /// The update data flag.
        /// </summary>
        private bool updateDataFlag = true;

        /// <summary>
        /// OpenGL renderer
        /// </summary>
        private PlotViewRenderer _renderer;

        /// <summary>
        /// Initializes a new instance of the <see cref="PlotView" /> class.
        /// </summary>
        /// <param name="context">The context.</param>
        /// <remarks>Use this constructor when creating the view from code.</remarks>
        public PlotViewGL(Context context) :
            base(context)
        {
            InitialiseRenderer();
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="PlotView" /> class.
        /// </summary>
        /// <param name="context">The context.</param>
        /// <param name="attrs">The attribute set.</param>
        /// <remarks>This constructor is called when inflating the view from XML.</remarks>
        public PlotViewGL(Context context, IAttributeSet attrs) :
            base(context, attrs)
        {
            InitialiseRenderer();
        }

        /// <summary>
        /// Initialise the opengl renderer
        /// </summary>
        private void InitialiseRenderer()
        {
            SetEGLContextClientVersion(2);
            SetZOrderOnTop(true);
            SetEGLConfigChooser(8, 8, 8, 8, 16, 0);
            Holder.SetFormat(Format.Translucent);
            _renderer = new PlotViewRenderer(this);
            SetRenderer(_renderer);
            RenderMode = Rendermode.WhenDirty;
        }

        /// <summary>
        /// Gets or sets the plot model.
        /// </summary>
        /// <value>The model.</value>
        public PlotModel Model
        {
            get
            {
                return this.model;
            }

            set
            {
                if (this.model != value)
                {
                    if (this.model != null)
                    {
                        ((IPlotModel)this.model).AttachPlotView(null);
                        this.model = null;
                    }

                    if (value != null)
                    {
                        ((IPlotModel)value).AttachPlotView(this);
                        this.model = value;
                    }

                    this.InvalidatePlot();
                }
            }
        }

        /// <summary>
        /// Gets or sets the plot controller.
        /// </summary>
        /// <value>The controller.</value>
        public IPlotController Controller { get; set; }

        /// <summary>
        /// Gets the actual model in the view.
        /// </summary>
        /// <value>
        /// The actual model.
        /// </value>
        Model IView.ActualModel
        {
            get
            {
                return this.Model;
            }
        }

        /// <summary>
        /// Gets the actual <see cref="PlotModel" /> of the control.
        /// </summary>
        public PlotModel ActualModel
        {
            get
            {
                return this.Model;
            }
        }

        /// <summary>
        /// Gets the actual controller.
        /// </summary>
        /// <value>
        /// The actual <see cref="IController" />.
        /// </value>
        IController IView.ActualController
        {
            get
            {
                return this.ActualController;
            }
        }

        /// <summary>
        /// Gets the coordinates of the client area of the view.
        /// </summary>
        public OxyRect ClientArea
        {
            get
            {
                return new OxyRect(0, 0, this.Width, this.Height);
            }
        }

        /// <summary>
        /// Gets the actual <see cref="IPlotController" /> of the control.
        /// </summary>
        /// <value>The actual plot controller.</value>
        public IPlotController ActualController
        {
            get
            {
                return this.Controller ?? (this.defaultController ?? (this.defaultController = new PlotController()));
            }
        }

        public object InvalidateLock
        {
            get
            {
                return invalidateLock;
            }
        }

        public bool IsModelInvalidated
        {
            get
            {
                return isModelInvalidated;
            }

            set
            {
                isModelInvalidated = value;
            }
        }

        public bool UpdateDataFlag
        {
            get
            {
                return updateDataFlag;
            }

            set
            {
                updateDataFlag = value;
            }
        }

        /// <summary>
        /// Hides the tracker.
        /// </summary>
        public void HideTracker()
        {
        }

        /// <summary>
        /// Hides the zoom rectangle.
        /// </summary>
        public void HideZoomRectangle()
        {
        }

        /// <summary>
        /// Invalidates the plot (not blocking the UI thread)
        /// </summary>
        /// <param name="updateData">if set to <c>true</c>, all data bindings will be updated.</param>
        public void InvalidatePlot(bool updateData = true)
        {
            lock (this.InvalidateLock)
            {
                this.IsModelInvalidated = true;
                this.UpdateDataFlag = this.UpdateDataFlag || updateData;
            }

            this.Invalidate();
        }

        /// <summary>
        /// Invalidate the whole view
        /// </summary>
        public override void Invalidate()
        {
            base.Invalidate();
            this.RequestRender();
        }

        /// <summary>
        /// Sets the cursor type.
        /// </summary>
        /// <param name="cursorType">The cursor type.</param>
        public void SetCursorType(CursorType cursorType)
        {
        }

        /// <summary>
        /// Shows the tracker.
        /// </summary>
        /// <param name="trackerHitResult">The tracker data.</param>
        public void ShowTracker(TrackerHitResult trackerHitResult)
        {
        }

        /// <summary>
        /// Shows the zoom rectangle.
        /// </summary>
        /// <param name="rectangle">The rectangle.</param>
        public void ShowZoomRectangle(OxyRect rectangle)
        {
        }

        /// <summary>
        /// Stores text on the clipboard.
        /// </summary>
        /// <param name="text">The text.</param>
        public void SetClipboardText(string text)
        {
        }

        /// <summary>
        /// Handles key down events.
        /// </summary>
        /// <param name="keyCode">The key code.</param>
        /// <param name="e">The event arguments.</param>
        /// <returns><c>true</c> if the event was handled.</returns>
        public override bool OnKeyDown(Keycode keyCode, KeyEvent e)
        {
            var handled = base.OnKeyDown(keyCode, e);
            if (!handled)
            {
                handled = this.ActualController.HandleKeyDown(this, e.ToKeyEventArgs());
            }

            return handled;
        }

        /// <summary>
        /// Handles touch screen motion events.
        /// </summary>
        /// <param name="e">The motion event arguments.</param>
        /// <returns><c>true</c> if the event was handled.</returns>
        public override bool OnTouchEvent(MotionEvent e)
        {
            var handled = base.OnTouchEvent(e);
            if (!handled)
            {
                switch (e.Action)
                {
                    case MotionEventActions.Down:
                        handled = this.OnTouchDownEvent(e);
                        break;
                    case MotionEventActions.Move:
                        handled = this.OnTouchMoveEvent(e);
                        break;
                    case MotionEventActions.Up:
                        handled = this.OnTouchUpEvent(e);
                        break;
                }
            }

            return handled;
        }

        /// <summary>
        /// Handles touch down events.
        /// </summary>
        /// <param name="e">The motion event arguments.</param>
        /// <returns><c>true</c> if the event was handled.</returns>
        private bool OnTouchDownEvent(MotionEvent e)
        {
            var args = e.ToTouchEventArgs(Scale);
            var handled = this.ActualController.HandleTouchStarted(this, args);
            this.previousTouchPoints = e.GetTouchPoints(Scale);
            return handled;
        }

        /// <summary>
        /// Handles touch move events.
        /// </summary>
        /// <param name="e">The motion event arguments.</param>
        /// <returns><c>true</c> if the event was handled.</returns>
        private bool OnTouchMoveEvent(MotionEvent e)
        {
            var currentTouchPoints = e.GetTouchPoints(Scale);
            var args = new OxyTouchEventArgs(currentTouchPoints, this.previousTouchPoints);
            var handled = this.ActualController.HandleTouchDelta(this, args);
            this.previousTouchPoints = currentTouchPoints;
            return handled;
        }

        /// <summary>
        /// Handles touch released events.
        /// </summary>
        /// <param name="e">The motion event arguments.</param>
        /// <returns><c>true</c> if the event was handled.</returns>
        private bool OnTouchUpEvent(MotionEvent e)
        {
            return this.ActualController.HandleTouchCompleted(this, e.ToTouchEventArgs(Scale));
        }
    }
}
