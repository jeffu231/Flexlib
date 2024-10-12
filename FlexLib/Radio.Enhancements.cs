using System.Collections.Immutable;

namespace Flex.Smoothlake.FlexLib;

/// <summary>
/// This class exists to help isolate the enhancments from the main code to make it easier to maintain and provide
/// clarity to the differences. Changes that can be isolated here will be. 
/// </summary>
public partial class Radio
{
    
    #region Meter enhancements
   
    /// <summary>
    /// A list of all the discovered meters.
    /// This is an add-on to the original library.
    /// </summary>
    /// <returns>An Immutable list of the known meters</returns>
    public ImmutableList<Meter> Meters
    {
        get
        {
            lock (_meters)
            {
                return _meters.ToImmutableList();
            }
        }
    }
    
    /// <summary>
    /// Used by the AddMeter method in the main Radio class.
    /// This is an add-on to the original library.
    /// </summary>
    /// <param name="meter"></param>
    /// <param name="data"></param>
    void MainFan_DataReady(Meter meter, float data)
    {
        OnMainFanDataReady(data);
    }
    
    /// <summary>
    /// This event is raised when there is new meter data for the Main Fan
    /// of the radio.  The data is in units of RPM.
    /// This is an add-on to the original library.
    /// </summary>
    public event MeterDataReadyEventHandler MainFanDataReady;
    private void OnMainFanDataReady(float data)
    {
        if (MainFanDataReady != null)
            MainFanDataReady(data);
    }
    
    #endregion

    #region Xvtr Routines

    /// <summary>
    /// The list of transverters defined in the radio
    /// This is an add-on to the original library.
    /// </summary>
    /// <returns> An Immutable list of Xvtr</returns>
    public ImmutableList<Xvtr> Xvtrs => _xvtrs.ToImmutableList();

    #endregion
}