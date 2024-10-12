# SmartSDR v3.x API (Flexlib) 

The latest is based on version 3.8.19.

This is a port of the Flexlib library that has been retargeted to support NET Core 8.0, .NET Framework 4.6.2 and .NET Framework 4.8.

Portions of the UiWpfFramework project have been excluded from the Net Core 8 build since they are not cross-platform, but all radio API functions work.

This port has been done to support my own station integration and is by no means an official product of Flex Radio. The core if it is based on the Flex provided libraries
and every attempt is made to maintain compatibility with those while providing additional value.

Nuget packages are provided to allow for easier integration into projects.

## Enhancements

### Meters

Logic was added to expose the list of Meters so API consumers can dynamically determine what meters are available and get the details of their definition.

### Main Fan Meter

The main fan meter has been exposed as a first class event data similar to the power, voltage, SWR, etc. This makes it easier to subscribe to that without waiting and 
and obtaining that meter directly by name.

### Transverters

Added a property to provide a list of transverters (Xvtr) that are known to the radio. This is not exposed in the original libraries. 
