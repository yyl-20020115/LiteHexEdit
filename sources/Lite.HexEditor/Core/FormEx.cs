﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Lite.HexEditor.Core
{
  public partial class FormEx : Form
  {
    // DPI at design time
    public const float DpiAtDesign = 96F;

    // New (current) DPI
    private float _dpiNew = 0;

    // Old (previous) DPI
    private float _dpiOld = 0;

    private float _factor;

    // Flag to set whether this window is being moved by user
    private bool _isBeingMoved = false;

    // Method for adjustment
    private ResizeMethod _method = ResizeMethod.Immediate;

    // Flag to set whether this window will be adjusted later
    private bool _willBeAdjusted = false;

    public FormEx()
    {
      //this.AutoScaleDimensions = new System.Drawing.SizeF(9F, 20F);
      this.Font = SystemFonts.MessageBoxFont;
      this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Dpi;
      this.Load += new System.EventHandler(this.MainForm_Load);
      this.ResizeBegin += new System.EventHandler(this.MainForm_ResizeBegin);
      this.ResizeEnd += new System.EventHandler(this.MainForm_ResizeEnd);
      this.Move += new System.EventHandler(this.MainForm_Move);
    }

    public event EventHandler FactorChanged;

    private enum DelayedState
    {
      Initial,
      Waiting,
      Resized,
      Aborted
    }

    private enum ResizeMethod
    {
      Immediate,
      Delayed
    }

    [DefaultValue(0), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public float DpiNew
    {
      get => _dpiNew;
      set => _dpiNew = value;
    }

    [DefaultValue(0), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public float DpiOld
    {
      get => _dpiOld;
      set => _dpiOld = value;
    }

    [DefaultValue(1), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public float Factor
    {
      get => _factor;
      private set
      {
        if (_factor == value)
          return;
        _factor = value;

        if (FactorChanged != null)
          FactorChanged(this, EventArgs.Empty);
      }
    }

    protected virtual void AdjustFont(float factor)
    {
      if (Util.DesignMode)
        return;

      var dic = GetChildControlFontSizes(this);

      CoreUtil.ScaleFont(this, factor);

      foreach (var item in dic)
      {
        // not affected by parent font?
        if (item.Key.Font.Size == item.Value)
        {
          CoreUtil.ScaleFont(item.Key, factor);
          continue;
        }
      }
    }

    // Adjust this window.
    protected virtual void AdjustWindow()
    {
      if (Util.DesignMode)
        return;

      if ((_dpiOld == 0) || (_dpiOld == _dpiNew))
        return; // Abort.

      float factor = _dpiNew / _dpiOld;

      //MessageBox.Show(string.Format("new{0}, old{1}, factor: {2}", dpiNew, dpiOld, factor));

      _dpiOld = _dpiNew;

      // Adjust location and size of Controls (except location of this window itself).
      this.Scale(new SizeF(factor, factor));

      // Adjust Font size of Controls.
      this.AdjustFont(factor);
    }

    // Catch window message of DPI change.
    protected override void WndProc(ref Message m)
    {
      base.WndProc(ref m);

      // Check if Windows 8.1 or newer and if not, ignore message.
      if (!IsWin81OrNewer())
        return;

      const int WM_DPICHANGED = 0x02e0; // 0x02E0 from WinUser.h

      if (m.Msg == WM_DPICHANGED)
      {
        // wParam
        short lo = Win32.GetLoWord(m.WParam.ToInt32());

        // lParam
        Win32.RECT r = (Win32.RECT)Marshal.PtrToStructure(m.LParam, typeof(Win32.RECT));

        // Hold new DPI as target for adjustment.
        _dpiNew = lo;

        switch (_method)
        {
          case ResizeMethod.Immediate:
            if (_dpiOld != lo)
            {
              MoveWindow();
              AdjustWindow();
            }
            break;

          case ResizeMethod.Delayed:
            if (_dpiOld != lo)
            {
              if (_isBeingMoved)
              {
                _willBeAdjusted = true;
              }
              else
              {
                AdjustWindow();
              }
            }
            else
            {
              if (_willBeAdjusted)
              {
                _willBeAdjusted = false;
              }
            }
            break;
        }
      }
    }

    // Adjust location, size and font size of Controls according to new DPI.
    private void AdjustWindowInitial()
    {
      // Hold initial DPI used at loading this window.
      DpiOld = this.CurrentAutoScaleDimensions.Width;

      // Check current DPI.
      DpiNew = GetDpiWindowMonitor();

      AdjustWindow();
    }

    private void FillChildControlFontSizes(Dictionary<Control, float> dic, Control parent)
    {
      foreach (Control child in parent.Controls)
      {
        dic.Add(child, child.Font.Size);
        FillChildControlFontSizes(dic, child);
      }
    }

    // Get child Controls in a specified Control.
    private Dictionary<Control, float> GetChildControlFontSizes(Control parent)
    {
      var dic = new Dictionary<Control, float>();
      FillChildControlFontSizes(dic, parent);
      return dic;
    }

    // Get DPI for all monitors by GetDeviceCaps.
    private float GetDpiDeviceMonitor()
    {
      int dpiX = 0;
      IntPtr screen = IntPtr.Zero;

      try
      {
        screen = Win32.GetDC(IntPtr.Zero);
        dpiX = Win32.GetDeviceCaps(screen, Win32.LOGPIXELSX);
      }
      finally
      {
        if (screen != IntPtr.Zero)
        {
          Win32.ReleaseDC(IntPtr.Zero, screen);
        }
      }

      return (float)dpiX;
    }

    // Get DPI of a specified monitor by GetDpiForMonitor.
    private float GetDpiSpecifiedMonitor(IntPtr handleMonitor)
    {
      // Check if GetDpiForMonitor function is available.
      if (!IsWin81OrNewer()) return this.CurrentAutoScaleDimensions.Width;

      // Get DPI.
      uint dpiX = 0;
      uint dpiY = 0;

      int result = Win32.GetDpiForMonitor(handleMonitor, Win32.Monitor_DPI_Type.MDT_Default, out dpiX, out dpiY);

      if (result != 0) // If not S_OK (= 0)
      {
        throw new Exception("Failed to get DPI of monitor containing this window.");
      }

      return (float)dpiX;
    }

    // Get DPI of monitor containing this window by GetDpiForMonitor.
    private float GetDpiWindowMonitor()
    {
      // Get handle to this window.
      IntPtr handleWindow = Process.GetCurrentProcess().MainWindowHandle;

      // Get handle to monitor.
      IntPtr handleMonitor = Win32.MonitorFromWindow(handleWindow, Win32.MONITOR_DEFAULTTOPRIMARY);

      // Get DPI.
      return GetDpiSpecifiedMonitor(handleMonitor);
    }

    // Get OS version in Double.
    private double GetVersion()
    {
      OperatingSystem os = Environment.OSVersion;

      return os.Version.Major + ((double)os.Version.Minor / 10);
    }

    // Check if current location of this window is good for delayed adjustment.
    private bool IsLocationGood()
    {
      if (_dpiOld == 0) return false; // Abort.

      float factor = _dpiNew / _dpiOld;

      // Prepare new rectangle shrinked or expanded sticking Left-Top corner.
      int widthDiff = (int)(this.ClientSize.Width * factor) - this.ClientSize.Width;
      int heightDiff = (int)(this.ClientSize.Height * factor) - this.ClientSize.Height;

      Win32.RECT rect = new Win32.RECT()
      {
        left = this.Bounds.Left,
        top = this.Bounds.Top,
        right = this.Bounds.Right + widthDiff,
        bottom = this.Bounds.Bottom + heightDiff
      };

      // Get handle to monitor that has the largest intersection with the rectangle.
      IntPtr handleMonitor = Win32.MonitorFromRect(ref rect, Win32.MONITOR_DEFAULTTONULL);

      if (handleMonitor != IntPtr.Zero)
      {
        // Check if DPI of the monitor matches.
        if (GetDpiSpecifiedMonitor(handleMonitor) == _dpiNew)
        {
          return true;
        }
      }

      return false;
    }

    // Check if OS is Windows 8.1 or newer.
    private bool IsWin81OrNewer()
    {
      // To get this value correctly, it is required to include ID of Windows 8.1 in the manifest file.
      return (6.3 <= GetVersion());
    }

    private void MainForm_Load(object sender, EventArgs e)
    {
      if (!Util.DesignMode)
        AdjustWindowInitial();
    }

    // Detect this window is moved.
    private void MainForm_Move(object sender, EventArgs e)
    {
      if (_willBeAdjusted && IsLocationGood())
      {
        _willBeAdjusted = false;

        AdjustWindow();
      }
    }

    // Detect user began moving this window.
    private void MainForm_ResizeBegin(object sender, EventArgs e)
    {
      _isBeingMoved = true;
    }

    // Detect user ended moving this window.
    private void MainForm_ResizeEnd(object sender, EventArgs e)
    {
      _isBeingMoved = false;
    }

    // Get new location of this window after DPI change.
    private void MoveWindow()
    {
      if (Util.DesignMode)
        return;

      if (_dpiOld == 0)
        return; // Abort.

      float factor = _dpiNew / _dpiOld;

      // Prepare new rectangles shrinked or expanded sticking four corners.
      int widthDiff = (int)(this.ClientSize.Width * factor) - this.ClientSize.Width;
      int heightDiff = (int)(this.ClientSize.Height * factor) - this.ClientSize.Height;

      List<Win32.RECT> rectList = new List<Win32.RECT>();

      // Left-Top corner
      rectList.Add(new Win32.RECT
      {
        left = this.Bounds.Left,
        top = this.Bounds.Top,
        right = this.Bounds.Right + widthDiff,
        bottom = this.Bounds.Bottom + heightDiff
      });

      // Right-Top corner
      rectList.Add(new Win32.RECT
      {
        left = this.Bounds.Left - widthDiff,
        top = this.Bounds.Top,
        right = this.Bounds.Right,
        bottom = this.Bounds.Bottom + heightDiff
      });

      // Left-Bottom corner
      rectList.Add(new Win32.RECT
      {
        left = this.Bounds.Left,
        top = this.Bounds.Top - heightDiff,
        right = this.Bounds.Right + widthDiff,
        bottom = this.Bounds.Bottom
      });

      // Right-Bottom corner
      rectList.Add(new Win32.RECT
      {
        left = this.Bounds.Left - widthDiff,
        top = this.Bounds.Top - heightDiff,
        right = this.Bounds.Right,
        bottom = this.Bounds.Bottom
      });

      // Get handle to monitor that has the largest intersection with each rectangle.
      for (int i = 0; i <= rectList.Count - 1; i++)
      {
        Win32.RECT rectBuf = rectList[i];

        IntPtr handleMonitor = Win32.MonitorFromRect(ref rectBuf, Win32.MONITOR_DEFAULTTONULL);

        if (handleMonitor != IntPtr.Zero)
        {
          // Check if at least Left-Top corner or Right-Top corner is inside monitors.
          IntPtr handleLeftTop = Win32.MonitorFromPoint(new Win32.POINT(rectBuf.left, rectBuf.top), Win32.MONITOR_DEFAULTTONULL);
          IntPtr handleRightTop = Win32.MonitorFromPoint(new Win32.POINT(rectBuf.right, rectBuf.top), Win32.MONITOR_DEFAULTTONULL);

          if ((handleLeftTop != IntPtr.Zero) || (handleRightTop != IntPtr.Zero))
          {
            // Check if DPI of the monitor matches.
            if (GetDpiSpecifiedMonitor(handleMonitor) == _dpiNew)
            {
              // Move this window.
              this.Location = new Point(rectBuf.left, rectBuf.top);

              break;
            }
          }
        }
      }
    }
  }
}
