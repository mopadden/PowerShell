/********************************************************************++
Copyright (c) Microsoft Corporation.  All rights reserved.
--********************************************************************/
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.IO;
using System.Management.Automation;
using System.Management.Automation.Provider;
using System.Text;
using Dbg = System.Management.Automation;

namespace System.Management.Automation
{
    /// <summary>
    /// Implements the interfaces used by navigation commands to work with
    /// the virtual drive system.
    /// </summary>
    internal sealed class LocationGlobber
    {
        #region Trace object

        /// <summary>
        /// An instance of the PSTraceSource class used for trace output
        /// using "LocationGlobber" as the category.
        /// </summary>
        [Dbg.TraceSourceAttribute(
             "LocationGlobber",
             "The location globber converts PowerShell paths with glob characters to zero or more paths.")]
        private static Dbg.PSTraceSource tracer =
            Dbg.PSTraceSource.GetTracer("LocationGlobber",
             "The location globber converts PowerShell paths with glob characters to zero or more paths.");

        /// <summary>
        /// User level tracing for path resolution.
        /// </summary>
        [Dbg.TraceSourceAttribute(
             "PathResolution",
             "Traces the path resolution algorithm.")]
        private static Dbg.PSTraceSource pathResolutionTracer =
            Dbg.PSTraceSource.GetTracer(
                "PathResolution",
                "Traces the path resolution algorithm.",
                false);

        #endregion Trace object

        #region Constructor

        /// <summary>
        /// Constructs an instance of the LocationGlobber from the current SessionState 
        /// </summary>
        /// 
        /// <param name="sessionState">
        /// The instance of session state on which this location globber acts.
        /// </param>
        /// 
        /// <exception cref="ArgumentNullException">
        /// If <paramref name="sessionState" /> is null.
        /// </exception>
        /// 
        internal LocationGlobber(SessionState sessionState)
        {
            if (sessionState == null)
            {
                throw PSTraceSource.NewArgumentNullException("sessionState");
            }

            this.sessionState = sessionState;
        } // LocationGlobber(SessionState)

        #endregion Constructor

        #region Public methods

        #region PowerShell paths from PowerShell path globbing
        /// <summary>
        /// Converts a PowerShell path containing glob characters to PowerShell paths that match
        /// the glob string.
        /// </summary>
        /// 
        /// <param name="path">
        /// A PowerShell path containing glob characters.
        /// </param>
        /// 
        /// <param name="allowNonexistingPaths">
        /// If true, a ItemNotFoundException will not be thrown for non-existing
        /// paths. Instead an appropriate path will be returned as if it did exist.
        /// </param>
        /// 
        /// <param name="providerInstance">
        /// The provider instance used to resolve the path.
        /// </param>
        /// 
        /// <returns>
        /// The PowerShell paths that match the glob string.
        /// </returns>
        /// 
        /// <exception cref="ArgumentNullException">
        /// If <paramref name="path"/> is null.
        /// </exception>
        /// 
        /// <exception cref="ProviderNotFoundException">
        /// If <paramref name="path"/> is a provider-qualified path 
        /// and the specified provider does not exist.
        /// </exception>
        /// 
        /// <exception cref="ProviderInvocationException">
        /// If the provider throws an exception when its MakePath gets
        /// called.
        /// </exception>
        /// 
        /// <exception cref="NotSupportedException">
        /// If the provider does not support multiple items.
        /// </exception>
        /// 
        /// <exception cref="InvalidOperationException">
        /// If the home location for the provider is not set and
        /// <paramref name="path"/> starts with a "~".
        /// </exception>
        /// 
        /// <exception cref="ItemNotFoundException">
        /// If <paramref name="path"/> does not contain glob characters and
        /// could not be found.
        /// </exception>
        /// 
        internal Collection<PathInfo> GetGlobbedMonadPathsFromMonadPath(
            string path,
            bool allowNonexistingPaths,
            out CmdletProvider providerInstance)
        {
            CmdletProviderContext context =
                new CmdletProviderContext(sessionState.Internal.ExecutionContext);

            return GetGlobbedMonadPathsFromMonadPath(path, allowNonexistingPaths, context, out providerInstance);
        } // GetGlobbedMonadPathsFromMonadPath

        /// <summary>
        /// Converts a PowerShell path containing glob characters to PowerShell paths that match
        /// the glob string.
        /// </summary>
        /// 
        /// <param name="path">
        /// A PowerShell path containing glob characters.
        /// </param>
        /// 
        /// <param name="allowNonexistingPaths">
        /// If true, a ItemNotFoundException will not be thrown for non-existing
        /// paths. Instead an appropriate path will be returned as if it did exist.
        /// </param>
        /// 
        /// <param name="context">
        /// The context under which the command is running.
        /// </param>
        /// 
        /// <param name="providerInstance">
        /// The instance of the provider used to resolve the path.
        /// </param>
        /// 
        /// <returns>
        /// The PowerShell paths that match the glob string.
        /// </returns>
        /// 
        /// <exception cref="ArgumentNullException">
        /// If <paramref name="path"/> or <paramref name="context"/> is null.
        /// </exception>
        /// 
        /// <exception cref="ProviderNotFoundException">
        /// If <paramref name="path"/> is a provider-qualified path 
        /// and the specified provider does not exist.
        /// </exception>
        /// 
        /// <exception cref="ProviderInvocationException">
        /// If the provider throws an exception when its MakePath gets
        /// called.
        /// </exception>
        /// 
        /// <exception cref="NotSupportedException">
        /// If the provider does not support multiple items.
        /// </exception>
        /// 
        /// <exception cref="InvalidOperationException">
        /// If the home location for the provider is not set and
        /// <paramref name="path"/> starts with a "~".
        /// </exception>
        /// 
        /// <exception cref="ItemNotFoundException">
        /// If <paramref name="path"/> does not contain glob characters and
        /// could not be found.
        /// </exception>
        /// 
        /// <exception cref="PipelineStoppedException">
        /// If <paramref name="context"/> has been signaled for
        /// StopProcessing.
        /// </exception>
        /// 
        internal Collection<PathInfo> GetGlobbedMonadPathsFromMonadPath(
            string path,
            bool allowNonexistingPaths,
            CmdletProviderContext context,
            out CmdletProvider providerInstance)
        {
            providerInstance = null;
            if (path == null)
            {
                throw PSTraceSource.NewArgumentNullException("path");
            }

            if (context == null)
            {
                throw PSTraceSource.NewArgumentNullException("context");
            }

            Collection<PathInfo> result;

            using (pathResolutionTracer.TraceScope("Resolving MSH path \"{0}\" to MSH path", path))
            {
                TraceFilters(context);

                // First check to see if the path starts with a ~ (home)

                if (IsHomePath(path))
                {
                    using (pathResolutionTracer.TraceScope("Resolving HOME relative path."))
                    {
                        path = GetHomeRelativePath(path);
                    }
                }

                // Now determine how to parse the path

                bool isProviderDirectPath = IsProviderDirectPath(path);
                bool isProviderQualifiedPath = IsProviderQualifiedPath(path);
                if (isProviderDirectPath || isProviderQualifiedPath)
                {
                    result =
                        ResolvePSPathFromProviderPath(
                            path,
                            context,
                            allowNonexistingPaths,
                            isProviderDirectPath,
                            isProviderQualifiedPath,
                            out providerInstance);
                }
                else
                {
                    result =
                        ResolveDriveQualifiedPath(
                            path,
                            context,
                            allowNonexistingPaths,
                            out providerInstance);
                }

                if (!allowNonexistingPaths &&
                    result.Count < 1 &&
                    !WildcardPattern.ContainsWildcardCharacters(path) &&
                    (context.Include == null || context.Include.Count == 0) &&
                    (context.Exclude == null || context.Exclude.Count == 0))
                {
                    // Since we are not globbing, throw an exception since
                    // the path doesn't exist

                    ItemNotFoundException pathNotFound =
                        new ItemNotFoundException(
                            path,
                            "PathNotFound",
                            SessionStateStrings.PathNotFound);

                    pathResolutionTracer.TraceError("Item does not exist: {0}", path);

                    throw pathNotFound;
                }
            }
            return result;
        } // GetGlobbedMonadPathsFromMonadPath

        private Collection<string> ResolveProviderPathFromProviderPath(
            string providerPath,
            string providerId,
            bool allowNonexistingPaths,
            CmdletProviderContext context,
            out CmdletProvider providerInstance
            )
        {
            // Check the provider capabilities before globbing
            providerInstance = sessionState.Internal.GetProviderInstance(providerId);
            ContainerCmdletProvider containerCmdletProvider = providerInstance as ContainerCmdletProvider;
            ItemCmdletProvider itemProvider = providerInstance as ItemCmdletProvider;

            Collection<string> stringResult = new Collection<string>();

            if (!context.SuppressWildcardExpansion)
            {
                // See if the provider will expand the wildcard
                if (CmdletProviderManagementIntrinsics.CheckProviderCapabilities(
                        ProviderCapabilities.ExpandWildcards,
                        providerInstance.ProviderInfo))
                {
                    pathResolutionTracer.WriteLine("Wildcard matching is being performed by the provider.");

                    // Only do the expansion if the path actually contains wildcard
                    // characters.
                    if ((itemProvider != null) &&
                        (WildcardPattern.ContainsWildcardCharacters(providerPath)))
                    {
                        stringResult = new Collection<string>(itemProvider.ExpandPath(providerPath, context));
                    }
                    else
                    {
                        stringResult.Add(providerPath);
                    }
                }
                else
                {
                    pathResolutionTracer.WriteLine("Wildcard matching is being performed by the engine.");

                    if (containerCmdletProvider != null)
                    {
                        // Since it is really a provider-internal path, use provider-to-provider globbing
                        // and then add back on the provider ID.

                        stringResult =
                            GetGlobbedProviderPathsFromProviderPath(
                                providerPath,
                                allowNonexistingPaths,
                                containerCmdletProvider,
                                context);
                    }
                    else
                    {
                        // For simple CmdletProvider instances, we can't resolve the paths any
                        // further, so just return the providerPath
                        stringResult.Add(providerPath);
                    }
                }
            }
            // They are suppressing wildcard expansion
            else
            {
                if (itemProvider != null)
                {
                    if (allowNonexistingPaths|| itemProvider.ItemExists(providerPath, context))
                    {
                        stringResult.Add(providerPath);
                    }
                }
                else
                {
                    stringResult.Add(providerPath);
                }
            }

            // Make sure this resolved to something
            if ((!allowNonexistingPaths) &&
                stringResult.Count < 1 &&
                !WildcardPattern.ContainsWildcardCharacters(providerPath) &&
                (context.Include == null || context.Include.Count == 0) &&
                (context.Exclude == null || context.Exclude.Count == 0))
            {
                ItemNotFoundException pathNotFound =
                    new ItemNotFoundException(
                        providerPath,
                        "PathNotFound",
                        SessionStateStrings.PathNotFound);

                pathResolutionTracer.TraceError("Item does not exist: {0}", providerPath);
                throw pathNotFound;
            }

            return stringResult;
        }

        private Collection<PathInfo> ResolvePSPathFromProviderPath(
            string path,
            CmdletProviderContext context,
            bool allowNonexistingPaths,
            bool isProviderDirectPath,
            bool isProviderQualifiedPath,
            out CmdletProvider providerInstance)
        {
            Collection<PathInfo> result = new Collection<PathInfo>();

            providerInstance = null;
            string providerId = null;
            PSDriveInfo drive = null;

            // The path is a provide direct path so use the current
            // provider and don't modify the path.

            string providerPath = null;

            if (isProviderDirectPath)
            {
                pathResolutionTracer.WriteLine("Path is PROVIDER-DIRECT");
                providerPath = path;
                providerId = sessionState.Path.CurrentLocation.Provider.Name;
            }
            else if (isProviderQualifiedPath)
            {
                pathResolutionTracer.WriteLine("Path is PROVIDER-QUALIFIED");
                providerPath = ParseProviderPath(path, out providerId);
            }

            pathResolutionTracer.WriteLine("PROVIDER-INTERNAL path: {0}", providerPath);
            pathResolutionTracer.WriteLine("Provider: {0}", providerId);

            Collection<string> stringResult = ResolveProviderPathFromProviderPath(
                providerPath,
                providerId,
                allowNonexistingPaths,
                context,
                out providerInstance
                );

            // Get the hidden drive for the provider
            drive = providerInstance.ProviderInfo.HiddenDrive;

            // Now fix the paths
            foreach (string globbedPath in stringResult)
            {
                string escapedPath = globbedPath;

                // Making sure to obey the StopProcessing.
                if (context.Stopping)
                {
                    throw new PipelineStoppedException();
                }

                string constructedProviderPath = null;

                if (IsProviderDirectPath(escapedPath))
                {
                    constructedProviderPath = escapedPath;
                }
                else
                {
                    constructedProviderPath =
                        String.Format(
                            System.Globalization.CultureInfo.InvariantCulture,
                            "{0}::{1}",
                            providerId,
                            escapedPath);
                }

                result.Add(new PathInfo(drive, providerInstance.ProviderInfo, constructedProviderPath, sessionState));
                pathResolutionTracer.WriteLine("RESOLVED PATH: {0}", constructedProviderPath);
            }

            return result;
        }

        private Collection<PathInfo> ResolveDriveQualifiedPath(
            string path,
            CmdletProviderContext context,
            bool allowNonexistingPaths,
            out CmdletProvider providerInstance)
        {
            providerInstance = null;
            PSDriveInfo drive = null;

            Collection<PathInfo> result = new Collection<PathInfo>();

            pathResolutionTracer.WriteLine("Path is DRIVE-QUALIFIED");

            string relativePath = GetDriveRootRelativePathFromPSPath(path, context, true, out drive, out providerInstance);

            Dbg.Diagnostics.Assert(
                drive != null,
                "GetDriveRootRelativePathFromPSPath should always return a valid drive");

            Dbg.Diagnostics.Assert(
                relativePath != null,
                "There should always be a way to generate a provider path for a " +
                "given path");

            pathResolutionTracer.WriteLine("DRIVE-RELATIVE path: {0}", relativePath);
            pathResolutionTracer.WriteLine("Drive: {0}", drive.Name);
            pathResolutionTracer.WriteLine("Provider: {0}", drive.Provider);

            // Associate the drive with the context

            context.Drive = drive;
            providerInstance = sessionState.Internal.GetContainerProviderInstance(drive.Provider);
            ContainerCmdletProvider containerCmdletProvider = providerInstance as ContainerCmdletProvider;
            ItemCmdletProvider itemProvider = providerInstance as ItemCmdletProvider;

            ProviderInfo provider = providerInstance.ProviderInfo;

            string userPath = null;
            string itemPath = null;

            if (drive.Hidden)
            {
                userPath = GetProviderQualifiedPath(relativePath, provider);
                itemPath = relativePath;
            }
            else
            {
                userPath = GetDriveQualifiedPath(relativePath, drive);
                itemPath = GetProviderPath(path, context);
            }
            pathResolutionTracer.WriteLine("PROVIDER path: {0}", itemPath);

            Collection<string> stringResult = new Collection<string>();

            if (! context.SuppressWildcardExpansion)
            {
                // See if the provider will expand the wildcard
                if (CmdletProviderManagementIntrinsics.CheckProviderCapabilities(
                        ProviderCapabilities.ExpandWildcards,
                        provider))
                {
                    pathResolutionTracer.WriteLine("Wildcard matching is being performed by the provider.");

                    // Only do the expansion if the path actually contains wildcard
                    // characters.
                    if ((itemProvider != null) &&
                        (WildcardPattern.ContainsWildcardCharacters(relativePath)))
                    {
                        foreach (string pathResult in itemProvider.ExpandPath(itemPath, context))
                        {
                            stringResult.Add(
                                GetDriveRootRelativePathFromProviderPath(pathResult, drive, context));
                        }
                    }
                    else
                    {
                        stringResult.Add(GetDriveRootRelativePathFromProviderPath(itemPath, drive, context));
                    }
                }
                else
                {
                    pathResolutionTracer.WriteLine("Wildcard matching is being performed by the engine.");

                    // Now perform the globbing
                    stringResult =
                        ExpandMshGlobPath(
                            relativePath,
                            allowNonexistingPaths,
                            drive,
                            containerCmdletProvider,
                            context);
                }
            }
            // They are suppressing wildcard expansion
            else
            {
                if (itemProvider != null)
                {
                    if (allowNonexistingPaths || itemProvider.ItemExists(itemPath, context))
                    {
                        stringResult.Add(userPath);
                    }
                }
                else
                {
                    stringResult.Add(userPath);
                }
            }

            // Make sure this resolved to something
            if ((!allowNonexistingPaths) &&
                stringResult.Count < 1 &&
                !WildcardPattern.ContainsWildcardCharacters(path) &&
                (context.Include == null || context.Include.Count == 0) &&
                (context.Exclude == null || context.Exclude.Count == 0))
            {
                ItemNotFoundException pathNotFound =
                    new ItemNotFoundException(
                        path,
                        "PathNotFound",
                        SessionStateStrings.PathNotFound);

                pathResolutionTracer.TraceError("Item does not exist: {0}", path);
                throw pathNotFound;
            }

            // Now fix the paths
            foreach (string expandedPath in stringResult)
            {
                // Make sure to obey StopProcessing
                if (context.Stopping)
                {
                    throw new PipelineStoppedException();
                }

                // Add the drive back into the path
                userPath = null;

                if (drive.Hidden)
                {
                    if (IsProviderDirectPath(expandedPath))
                    {
                        userPath = expandedPath;
                    }
                    else
                    {
                        userPath =
                            LocationGlobber.GetProviderQualifiedPath(
                                expandedPath,
                                provider);
                    }
                }
                else
                {
                    userPath =
                        LocationGlobber.GetDriveQualifiedPath(
                                expandedPath,
                                drive);
                }
                
                result.Add(new PathInfo(drive, provider, userPath, sessionState));
                pathResolutionTracer.WriteLine("RESOLVED PATH: {0}", userPath);
            }

            return result;
        }

        #endregion PowerShell paths from PowerShell path globbing

        #region Provider paths from PowerShell path globbing

        /// <summary>
        /// Converts a PowerShell path containing glob characters to the provider
        /// specific paths matching the glob strings.
        /// </summary>
        /// 
        /// <param name="path">
        /// A PowerShell path containing glob characters.
        /// </param>
        /// 
        /// <param name="allowNonexistingPaths">
        /// If true, a ItemNotFoundException will not be thrown for non-existing
        /// paths. Instead an appropriate path will be returned as if it did exist.
        /// </param>
        /// 
        /// <param name="provider">
        /// Returns the information of the provider that was used to do the globbing.
        /// </param>
        /// 
        /// <param name="providerInstance">
        /// The instance of the provider used to resolve the path.
        /// </param>
        /// 
        /// <returns>
        /// An array of provider specific paths that matched the PowerShell glob path.
        /// </returns>
        /// 
        /// <exception cref="ArgumentNullException">
        /// If <paramref name="path"/> is null.
        /// </exception>
        /// 
        /// <exception cref="ProviderNotFoundException">
        /// If the path is a provider-qualified path for a provider that is
        /// not loaded into the system.
        /// </exception>
        /// 
        /// <exception cref="DriveNotFoundException">
        /// If the <paramref name="path"/> refers to a drive that could not be found.
        /// </exception>
        /// 
        /// <exception cref="ProviderInvocationException">
        /// If the provider used to build the path threw an exception.
        /// </exception>
        /// 
        /// <exception cref="NotSupportedException">
        /// If the provider that the <paramref name="path"/> represents is not a NavigationCmdletProvider
        /// or ContainerCmdletProvider.
        /// </exception>
        /// 
        /// <exception cref="InvalidOperationException">
        /// If the <paramref name="path"/> starts with "~" and the home location is not set for 
        /// the provider.
        /// </exception>
        /// 
        /// <exception cref="ProviderInvocationException">
        /// If the provider associated with the <paramref name="path"/> threw an
        /// exception when its GetParentPath or MakePath was called while
        /// processing the <paramref name="path"/>.
        /// </exception>
        /// 
        /// <exception cref="ItemNotFoundException">
        /// If <paramref name="path"/> does not contain glob characters and
        /// could not be found.
        /// </exception>
        /// 
        /// <exception>
        /// Any exception can be thrown by the provider that is called to build
        /// the provider path.
        /// </exception>
        /// 
        internal Collection<string> GetGlobbedProviderPathsFromMonadPath(
            string path,
            bool allowNonexistingPaths,
            out ProviderInfo provider,
            out CmdletProvider providerInstance)
        {
            providerInstance = null;
            if (path == null)
            {
                throw PSTraceSource.NewArgumentNullException("path");
            }

            CmdletProviderContext context =
                new CmdletProviderContext(sessionState.Internal.ExecutionContext);

            return GetGlobbedProviderPathsFromMonadPath(path, allowNonexistingPaths, context, out provider, out providerInstance);
        } // GetGlobbedProviderPathsFromMonadPath

        /// <summary>
        /// Converts a PowerShell path containing glob characters to the provider
        /// specific paths matching the glob strings.
        /// </summary>
        /// 
        /// <param name="path">
        /// A PowerShell path containing glob characters.
        /// </param>
        /// 
        /// <param name="allowNonexistingPaths">
        /// If true, a ItemNotFoundException will not be thrown for non-existing
        /// paths. Instead an appropriate path will be returned as if it did exist.
        /// </param>
        /// 
        /// <param name="context">
        /// The context under which the command is running.
        /// </param>
        /// 
        /// <param name="provider">
        /// Returns the information of the provider that was used to do the globbing.
        /// </param>
        /// 
        /// <param name="providerInstance">
        /// The instance of the provider used to resolve the path.
        /// </param>
        /// 
        /// <returns>
        /// An array of provider specific paths that matched the PowerShell glob path.
        /// </returns>
        /// 
        /// <exception cref="ArgumentNullException">
        /// If <paramref name="path"/> or <paramref name="context"/> is null.
        /// </exception>
        /// 
        /// <exception cref="ProviderNotFoundException">
        /// If the path is a provider-qualified path for a provider that is
        /// not loaded into the system.
        /// </exception>
        /// 
        /// <exception cref="DriveNotFoundException">
        /// If the <paramref name="path"/> refers to a drive that could not be found.
        /// </exception>
        /// 
        /// <exception cref="ProviderInvocationException">
        /// If the provider used to build the path threw an exception.
        /// </exception>
        /// 
        /// <exception cref="NotSupportedException">
        /// If the provider that the <paramref name="path"/> represents is not a NavigationCmdletProvider
        /// or ContainerCmdletProvider.
        /// </exception>
        /// 
        /// <exception cref="InvalidOperationException">
        /// If the <paramref name="path"/> starts with "~" and the home location is not set for 
        /// the provider.
        /// </exception>
        /// 
        /// <exception cref="ProviderInvocationException">
        /// If the provider associated with the <paramref name="path"/> threw an
        /// exception when its GetParentPath or MakePath was called while
        /// processing the <paramref name="path"/>.
        /// </exception>
        /// 
        /// <exception cref="ItemNotFoundException">
        /// If <paramref name="path"/> does not contain glob characters and
        /// could not be found.
        /// </exception>
        /// 
        /// <exception>
        /// Any exception can be thrown by the provider that is called to build
        /// the provider path.
        /// </exception>
        /// 
        internal Collection<string> GetGlobbedProviderPathsFromMonadPath(
            string path,
            bool allowNonexistingPaths,
            CmdletProviderContext context,
            out ProviderInfo provider,
            out CmdletProvider providerInstance)
        {
            if (path == null)
            {
                throw PSTraceSource.NewArgumentNullException("path");
            }

            if (context == null)
            {
                throw PSTraceSource.NewArgumentNullException("context");
            }

            using (pathResolutionTracer.TraceScope("Resolving MSH path \"{0}\" to PROVIDER-INTERNAL path", path))
            {
                TraceFilters(context);

                // Remove the drive from the context if this path is not associated with a drive
                if (IsProviderQualifiedPath(path))
                {
                    context.Drive = null;
                }

                PSDriveInfo drive = null;
                string providerPath = GetProviderPath(path, context, out provider, out drive);

                if (providerPath == null)
                {
                    providerInstance = null;
                    tracer.WriteLine("provider returned a null path so return an empty array");

                    pathResolutionTracer.WriteLine("Provider '{0}' returned null", provider);
                    return new Collection<string>();
                }

                if (drive != null)
                {
                    context.Drive = drive;
                }


                Collection<string> paths = new Collection<string>();

                foreach (PathInfo currentPath in
                    GetGlobbedMonadPathsFromMonadPath(
                        path,
                        allowNonexistingPaths,
                        context,
                        out providerInstance))
                {
                    paths.Add(currentPath.ProviderPath);
                }

                return paths;
            }
        } // GetGlobbedProviderPathsFromMonadPath

        #endregion Provider paths from Monad path globbing

        #region Provider paths from provider path globbing

        /// <summary>
        /// Given a provider specific path that contains glob characters, this method
        /// will perform the globbing using the specified provider and return the
        /// matching provider specific paths.
        /// </summary>
        /// 
        /// <param name="path">
        /// The path containing the glob characters to resolve.
        /// </param>
        /// 
        /// <param name="allowNonexistingPaths">
        /// If true, a ItemNotFoundException will not be thrown for non-existing
        /// paths. Instead an appropriate path will be returned as if it did exist.
        /// </param>
        /// 
        /// <param name="providerId">
        /// The ID of the provider to use to do the resolution.
        /// </param>
        ///
        /// <param name="providerInstance">
        /// The instance of the provider that was used to resolve the path.
        /// </param>
        /// 
        /// <returns>
        /// An array of provider specific paths that match the glob path.
        /// </returns>
        /// 
        /// <exception cref="ArgumentNullException">
        /// If <paramref name="path"/> is null.
        /// </exception>
        /// 
        /// <exception cref="ProviderNotFoundException">
        /// If <paramref name="providerId"/> references a provider that does not exist.
        /// </exception>
        /// 
        /// <exception cref="NotSupportedException">
        /// If the <paramref name="providerId"/> references a provider that is not
        /// a ContainerCmdletProvider.
        /// </exception>
        /// 
        /// <exception cref="ProviderInvocationException">
        /// If the provider used to build the path threw an exception.
        /// </exception>
        /// 
        /// <exception cref="InvalidOperationException">
        /// If the <paramref name="path"/> starts with "~" and the home location is not set for 
        /// the provider.
        /// </exception>
        /// 
        /// <exception cref="ItemNotFoundException">
        /// If <paramref name="path"/> does not contain glob characters and
        /// could not be found.
        /// </exception>
        /// 
        /// <exception>
        /// Any exception can be thrown by the provider that is called to build
        /// the provider path.
        /// </exception>
        /// 
        internal Collection<string> GetGlobbedProviderPathsFromProviderPath(
            string path,
            bool allowNonexistingPaths,
            string providerId,
            out CmdletProvider providerInstance)
        {
            providerInstance = null;

            if (path == null)
            {
                throw PSTraceSource.NewArgumentNullException("path");
            }

            CmdletProviderContext context =
                new CmdletProviderContext(sessionState.Internal.ExecutionContext);

            Collection<string> results =
                GetGlobbedProviderPathsFromProviderPath(
                    path,
                    allowNonexistingPaths,
                    providerId,
                    context,
                    out providerInstance);

            if (context.HasErrors())
            {
                // Throw the first error
                ErrorRecord errorRecord = context.GetAccumulatedErrorObjects()[0];

                if (errorRecord != null)
                {
                    throw errorRecord.Exception;
                }
            }
            return results;
        } // GetGlobbedProviderPathsFromProviderPath

        /// <summary>
        /// Given a provider specific path that contains glob characters, this method
        /// will perform the globbing using the specified provider and return the
        /// matching provider specific paths.
        /// </summary>
        /// 
        /// <param name="path">
        /// The path containing the glob characters to resolve. The path must be in the
        /// form providerId::providerPath.
        /// </param>
        /// 
        /// <param name="allowNonexistingPaths">
        /// If true, a ItemNotFoundException will not be thrown for non-existing
        /// paths. Instead an appropriate path will be returned as if it did exist.
        /// </param>
        /// 
        /// <param name="providerId">
        /// The provider identifier for the provider to use to do the globbing.
        /// </param>
        /// 
        /// <param name="context">
        /// The context under which the command is occurring.
        /// </param>
        /// 
        /// <param name="providerInstance">
        /// An instance of the provider that was used to perform the globbing.
        /// </param>
        /// 
        /// <returns>
        /// An array of provider specific paths that match the glob path.
        /// </returns>
        /// 
        /// <exception cref="ArgumentNullException">
        /// If <paramref name="path"/>, <paramref name="providerId"/>, or
        /// <paramref name="context"/> is null.
        ///  </exception>
        /// 
        /// <exception cref="ProviderNotFoundException">
        /// If <paramref name="providerId"/> references a provider that does not exist.
        /// </exception>
        /// 
        /// <exception cref="NotSupportedException">
        /// If the <paramref name="providerId"/> references a provider that is not
        /// a ContainerCmdletProvider.
        /// </exception>
        /// 
        /// <exception cref="ProviderInvocationException">
        /// If the provider used to build the path threw an exception.
        /// </exception>
        /// 
        /// <exception cref="InvalidOperationException">
        /// If the <paramref name="path"/> starts with "~" and the home location is not set for 
        /// the provider.
        /// </exception>
        /// 
        /// <exception>
        /// Any exception can be thrown by the provider that is called to build
        /// the provider path.
        /// </exception>
        /// 
        internal Collection<string> GetGlobbedProviderPathsFromProviderPath(
            string path,
            bool allowNonexistingPaths,
            string providerId,
            CmdletProviderContext context,
            out CmdletProvider providerInstance)
        {
            providerInstance = null;

            if (path == null)
            {
                throw PSTraceSource.NewArgumentNullException("path");
            }

            if (providerId == null)
            {
                throw PSTraceSource.NewArgumentNullException("providerId");
            }

            if (context == null)
            {
                throw PSTraceSource.NewArgumentNullException("context");
            }

            using (pathResolutionTracer.TraceScope("Resolving PROVIDER-INTERNAL path \"{0}\" to PROVIDER-INTERNAL path", path))
            {
                TraceFilters(context);

                return ResolveProviderPathFromProviderPath(
                    path,
                    providerId,
                    allowNonexistingPaths,
                    context,
                    out providerInstance);
            }
        } // GetGlobbedProviderPathsFromProviderPath


        #endregion Provider path to provider paths globbing


        #region Path manipulation

        /// <summary>
        /// Gets a provider specific path when given an Msh path without resolving the
        /// glob characters.
        /// </summary>
        /// 
        /// <param name="path">
        /// An Msh path.
        /// </param>
        /// 
        /// <returns>
        /// A provider specifc path that the Msh path represents.
        /// </returns>
        /// 
        /// <exception cref="ArgumentNullException">
        /// If <paramref name="path"/> is null.
        /// </exception>
        /// 
        /// <exception cref="ProviderNotFoundException">
        /// If the path is a provider-qualified path for a provider that is
        /// not loaded into the system.
        /// </exception>
        /// 
        /// <exception cref="DriveNotFoundException">
        /// If the <paramref name="path"/> refers to a drive that could not be found.
        /// </exception>
        /// 
        /// <exception cref="ProviderInvocationException">
        /// If the provider used to build the path threw an exception.
        /// </exception>
        /// 
        /// <exception cref="NotSupportedException">
        /// If the provider that the <paramref name="path"/> represents is not a NavigationCmdletProvider
        /// or ContainerCmdletProvider.
        /// </exception>
        /// 
        /// <exception cref="InvalidOperationException">
        /// If the <paramref name="path"/> starts with "~" and the home location is not set for 
        /// the provider.
        /// </exception>
        /// 
        /// <exception cref="ProviderInvocationException">
        /// If the provider specified by <paramref name="path"/> threw an
        /// exception.
        /// </exception>
        /// 
        /// <exception>
        /// Any exception can be thrown by the provider that is called to build
        /// the provider path.
        /// </exception>
        /// 
        internal string GetProviderPath(string path)
        {
            ProviderInfo provider = null;
            return GetProviderPath(path, out provider);
        } // GetProviderPath


        /// <summary>
        /// Gets a provider specific path when given an Msh path without resolving the
        /// glob characters.
        /// </summary>
        /// 
        /// <param name="path">
        /// An Msh path.
        /// </param>
        /// 
        /// <param name="provider">
        /// The information of the provider that was used to resolve the path.
        /// </param>
        /// 
        /// <returns>
        /// A provider specifc path that the Msh path represents.
        /// </returns>
        /// 
        /// <exception cref="ArgumentNullException">
        /// If <paramref name="path"/> is null.
        /// </exception>
        /// 
        /// <exception cref="ProviderNotFoundException">
        /// If the path is a provider-qualified path for a provider that is
        /// not loaded into the system.
        /// </exception>
        /// 
        /// <exception cref="DriveNotFoundException">
        /// If the <paramref name="path"/> refers to a drive that could not be found.
        /// </exception>
        /// 
        /// <exception cref="ProviderInvocationException">
        /// If the provider used to build the path threw an exception.
        /// </exception>
        /// 
        /// <exception cref="NotSupportedException">
        /// If the provider that the <paramref name="path"/> represents is not a NavigationCmdletProvider
        /// or ContainerCmdletProvider.
        /// </exception>
        /// 
        /// <exception cref="InvalidOperationException">
        /// If the <paramref name="path"/> starts with "~" and the home location is not set for 
        /// the provider.
        /// </exception>
        /// 
        /// <exception cref="ProviderInvocationException">
        /// If the provider specified by <paramref name="provider"/> threw an
        /// exception when its GetParentPath or MakePath was called while
        /// processing the <paramref name="path"/>.
        /// </exception>
        /// 
        /// <exception>
        /// Any exception can be thrown by the provider that is called to build
        /// the provider path.
        /// </exception>
        /// 
        internal string GetProviderPath(string path, out ProviderInfo provider)
        {
            if (path == null)
            {
                throw PSTraceSource.NewArgumentNullException("path");
            }

            CmdletProviderContext context =
                new CmdletProviderContext(sessionState.Internal.ExecutionContext);

            PSDriveInfo drive = null;
            provider = null;

            string result = GetProviderPath(path, context, out provider, out drive);

            if (context.HasErrors())
            {
                Collection<ErrorRecord> errors = context.GetAccumulatedErrorObjects();

                if (errors != null &&
                    errors.Count > 0)
                {
                    throw errors[0].Exception;
                }
            }

            return result;
        } // GetProviderPath

        /// <summary>
        /// Gets a provider specific path when given an Msh path without resolving the
        /// glob characters.
        /// </summary>
        /// 
        /// <param name="path">
        /// An Msh path.
        /// </param>
        /// 
        /// <param name="context">
        /// The context of the command.
        /// </param>
        /// 
        /// <returns>
        /// A provider specifc path that the Msh path represents.
        /// </returns>
        /// 
        /// <exception cref="ArgumentNullException">
        /// If <paramref name="path"/> is null.
        /// </exception>
        /// 
        /// <exception cref="ProviderNotFoundException">
        /// If the path is a provider-qualified path for a provider that is
        /// not loaded into the system.
        /// </exception>
        /// 
        /// <exception cref="DriveNotFoundException">
        /// If the <paramref name="path"/> refers to a drive that could not be found.
        /// </exception>
        /// 
        /// <exception cref="ProviderInvocationException">
        /// If the provider used to build the path threw an exception.
        /// </exception>
        /// 
        /// <exception cref="NotSupportedException">
        /// If the provider that the <paramref name="path"/> represents is not a NavigationCmdletProvider
        /// or ContainerCmdletProvider.
        /// </exception>
        /// 
        /// <exception cref="InvalidOperationException">
        /// If the <paramref name="path"/> starts with "~" and the home location is not set for 
        /// the provider.
        /// </exception>
        /// 
        /// <exception cref="ProviderInvocationException">
        /// If the provider specified by <paramref name="provider"/> threw an
        /// exception when its GetParentPath or MakePath was called while
        /// processing the <paramref name="path"/>.
        /// </exception>
        /// 
        /// <exception>
        /// Any exception can be thrown by the provider that is called to build
        /// the provider path.
        /// </exception>
        /// 
        internal string GetProviderPath(string path, CmdletProviderContext context)
        {
            if (path == null)
            {
                throw PSTraceSource.NewArgumentNullException("path");
            }

            PSDriveInfo drive = null;
            ProviderInfo provider = null;

            string result = GetProviderPath(path, context, out provider, out drive);

            return result;
        } // GetProviderPath

        /// <summary>
        /// Returns a provider specific path for given PowerShell path.
        /// </summary>
        /// 
        /// <param name="path">
        /// Either a PowerShell path or a provider path in the form providerId::providerPath
        /// </param>
        /// 
        /// <param name="context">
        /// The command context under which this operation is occurring.
        /// </param>
        /// 
        /// <param name="provider">
        /// This parameter is filled with the provider information for the given path.
        /// </param>
        /// 
        /// <param name="drive">
        /// This parameter is filled with the PowerShell drive that represents the given path. If a 
        /// provider path is given drive will be null.
        /// </param>
        /// 
        /// <returns>
        /// The provider specific path generated from the given path.
        /// </returns>
        /// 
        /// <exception cref="ArgumentNullException">
        /// If <paramref name="path"/> or <paramref name="context"/> is null.
        /// </exception>
        /// 
        /// <exception cref="ProviderNotFoundException">
        /// If the path is a provider-qualified path for a provider that is
        /// not loaded into the system.
        /// </exception>
        /// 
        /// <exception cref="DriveNotFoundException">
        /// If the <paramref name="path"/> refers to a drive that could not be found.
        /// </exception>
        /// 
        /// <exception cref="ProviderInvocationException">
        /// If the provider used to build the path threw an exception.
        /// </exception>
        /// 
        /// <exception cref="NotSupportedException">
        /// If the provider that the <paramref name="path"/> represents is not a NavigationCmdletProvider
        /// or ContainerCmdletProvider.
        /// </exception>
        /// 
        /// <exception cref="InvalidOperationException">
        /// If the <paramref name="path"/> starts with "~" and the home location is not set for 
        /// the provider.
        /// </exception>
        /// 
        /// <exception cref="ProviderInvocationException">
        /// If the provider specified by <paramref name="provider"/> threw an
        /// exception when its GetParentPath or MakePath was called while
        /// processing the <paramref name="path"/>.
        /// </exception>
        /// 
        internal string GetProviderPath(
            string path,
            CmdletProviderContext context,
            out ProviderInfo provider,
            out PSDriveInfo drive)
        {
            return GetProviderPath(
                path,
                context,
                false,
                out provider,
                out drive);
        }

        /// <summary>
        /// Returns a provider specific path for given PowerShell path.
        /// </summary>
        /// <param name="path">Path to resolve</param>
        /// <param name="context">Cmdlet contet</param>
        /// <param name="isTrusted">When true bypass trust check</param>
        /// <param name="provider">Provider</param>
        /// <param name="drive">Drive</param>
        /// <returns></returns>
        internal string GetProviderPath(
            string path,
            CmdletProviderContext context,
            bool isTrusted,
            out ProviderInfo provider,
            out PSDriveInfo drive)
        {
            if (path == null)
            {
                throw PSTraceSource.NewArgumentNullException("path");
            }

            if (context == null)
            {
                throw PSTraceSource.NewArgumentNullException("context");
            }

            string result = null;
            provider = null;
            drive = null;

            // First check to see if the path starts with a ~ (home)
            if (IsHomePath(path))
            {
                using (pathResolutionTracer.TraceScope("Resolving HOME relative path."))
                {
                    path = GetHomeRelativePath(path);
                }
            }

            // Now check to see if it is a provider-direct path (starts with // or \\)
            if (IsProviderDirectPath(path))
            {
                pathResolutionTracer.WriteLine("Path is PROVIDER-DIRECT");

                // just return the path directly using the current provider

                result = path;
                drive = null;
                provider = sessionState.Path.CurrentLocation.Provider;

                pathResolutionTracer.WriteLine("PROVIDER-INTERNAL path: {0}", result);
                pathResolutionTracer.WriteLine("Provider: {0}", provider);
            }
            else if (IsProviderQualifiedPath(path))
            {
                pathResolutionTracer.WriteLine("Path is PROVIDER-QUALIFIED");

                string providerId = null;
                result = ParseProviderPath(path, out providerId);
                drive = null;

                // Get the provider info
                provider = sessionState.Internal.GetSingleProvider(providerId);

                pathResolutionTracer.WriteLine("PROVIDER-INTERNAL path: {0}", result);
                pathResolutionTracer.WriteLine("Provider: {0}", provider);
            }
            else
            {
                pathResolutionTracer.WriteLine("Path is DRIVE-QUALIFIED");

                CmdletProvider providerInstance = null;
                string relativePath = GetDriveRootRelativePathFromPSPath(path, context, false, out drive, out providerInstance);

                Dbg.Diagnostics.Assert(
                    drive != null,
                    "GetDriveRootRelativePathFromPSPath should always return a valid drive");

                Dbg.Diagnostics.Assert(
                    relativePath != null,
                    "There should always be a way to generate a provider path for a " +
                    "given path");

                pathResolutionTracer.WriteLine("DRIVE-RELATIVE path: {0}", relativePath);
                pathResolutionTracer.WriteLine("Drive: {0}", drive.Name);
                pathResolutionTracer.WriteLine("Provider: {0}", drive.Provider);

                // Associate the drive with the context

                context.Drive = drive;

                if (drive.Hidden)
                {
                    result = relativePath;
                }
                else
                {
                    result = GetProviderSpecificPath(drive, relativePath, context);
                }
                provider = drive.Provider;

            }

            pathResolutionTracer.WriteLine("RESOLVED PATH: {0}", result);

            // If this is a private provider, don't allow access to it directly from the runspace.
            if ((provider != null) &&
                (context != null) &&
                (context.MyInvocation != null) &&
                (context.ExecutionContext != null) &&
                (context.ExecutionContext.InitialSessionState != null))
            {
                foreach (Runspaces.SessionStateProviderEntry sessionStateProvider in context.ExecutionContext.InitialSessionState.Providers[provider.Name])
                {
                    if (!isTrusted &&
                        (sessionStateProvider.Visibility == SessionStateEntryVisibility.Private) &&
                        (context.MyInvocation.CommandOrigin == CommandOrigin.Runspace))
                    {
                        pathResolutionTracer.WriteLine("Provider is private: {0}", provider.Name);

                        throw new ProviderNotFoundException(
                            provider.Name,
                            SessionStateCategory.CmdletProvider,
                            "ProviderNotFound",
                            SessionStateStrings.ProviderNotFound);
                    }
                }
            }

            return result;
        } // GetProviderPath

        /// <summary>
        /// Determines if the specified path is a provider. This is done by looking for
        /// two colons in a row. Anything before the colons is considered the provider ID,
        /// and everything after is considered a namespace specific path.
        /// </summary>
        /// 
        /// <param name="path">
        /// The path to check to see if it is a provider path.
        /// </param>
        /// 
        /// <returns>
        /// True if the path is a provider path, false otherwise.
        /// </returns>
        /// 
        /// <exception cref="ArgumentNullException">
        /// If <paramref name="path"/> is null.
        /// </exception>
        /// 
        internal static bool IsProviderQualifiedPath(string path)
        {
            string providerId = null;
            return IsProviderQualifiedPath(path, out providerId);
        }

        /// <summary>
        /// Determines if the specified path is a provider. This is done by looking for
        /// two colons in a row. Anything before the colons is considered the provider ID,
        /// and everything after is considered a namespace specific path.
        /// </summary>
        /// 
        /// <param name="path">
        /// The path to check to see if it is a provider path.
        /// </param>
        /// 
        /// <param name="providerId">
        /// The name of the provider if the path is a provider qualified path.
        /// </param>
        ///
        /// <returns>
        /// True if the path is a provider path, false otherwise.
        /// </returns>
        /// 
        /// <exception cref="ArgumentNullException">
        /// If <paramref name="path"/> is null.
        /// </exception>
        /// 
        internal static bool IsProviderQualifiedPath(string path, out string providerId)
        {
            // Verify parameters

            if (path == null)
            {
                throw PSTraceSource.NewArgumentNullException("path");
            }

            providerId = null;
            bool result = false;

            do
            {
                if (path.Length == 0)
                {
                    // The current working directory is specified

                    result = false;
                    break;
                }

                if (path.StartsWith(@".\", StringComparison.Ordinal) ||
                    path.StartsWith(@"./", StringComparison.Ordinal))
                {
                    // The .\ prefix basically escapes anything that follows
                    // so treat it as a relative path no matter what comes
                    // after it.

                    result = false;
                    break;
                }

                int index = path.IndexOf(':');
                if (index == -1 || index + 1 >= path.Length || path[index + 1] != ':')
                {
                    // If there is no : then the path is relative to the
                    // current working drive

                    result = false;
                    break;
                }

                // If the :: is the first two character in the path then we
                // must assume that it is part of the path, and not
                // delimiting the drive name.

                if (index > 0)
                {
                    result = true;

                    // Get the provider ID

                    providerId = path.Substring(0, index);

                    tracer.WriteLine("providerId = {0}", providerId);
                }

            } while (false);

            return result;
        } // IsProviderQualifiedPath

        /// <summary>
        /// Determines if the given path is relative or absolute
        /// </summary>
        /// 
        /// <param name="path">
        /// The path used in the determination
        /// </param>
        /// 
        /// <returns>
        /// true if the path is an absolute path, false otherwise.
        /// </returns>
        /// 
        /// <exception cref="ArgumentNullException">
        /// If <paramref name="path"/> is null.
        /// </exception>
        /// 
        internal static bool IsAbsolutePath(string path)
        {
            // Verify parameters

            if (path == null)
            {
                throw PSTraceSource.NewArgumentNullException("path");
            }

            bool result = false;

            do
            {
                if (path.Length == 0)
                {
                    // The current working directory is specified

                    result = false;
                    break;
                }

                if (path.StartsWith(@".\", StringComparison.Ordinal))
                {
                    // The .\ prefix basically escapes anything that follows
                    // so treat it as a relative path no matter what comes
                    // after it.

                    result = false;
                    break;
                }

                int index = path.IndexOf(":", StringComparison.Ordinal);

                if (index == -1)
                {
                    // If there is no : then the path is relative to the
                    // current working drive

                    result = false;
                    break;
                }

                // If the : is the first character in the path then we
                // must assume that it is part of the path, and not
                // delimiting the drive name.

                if (index > 0)
                {
                    // We must have a drive specified

                    result = true;
                }

            } while (false);

            return result;

        } // IsAbsolutePath

        /// <summary>
        /// Determines if the given path is relative or absolute
        /// </summary>
        /// 
        /// <param name="path">
        /// The path used in the determination
        /// </param>
        /// 
        /// <param name="driveName">
        /// If the path is absolute, this out parameter will be the
        /// drive name of the drive that is referenced.
        /// </param>
        /// 
        /// <returns>
        /// true if the path is an absolute path, false otherwise.
        /// </returns>
        /// 
        internal bool IsAbsolutePath(string path, out string driveName)
        {
            // Verify parameters

            if (path == null)
            {
                throw PSTraceSource.NewArgumentNullException("path");
            }

            bool result = false;


            if (sessionState.Drive.Current != null)
            {
                driveName = sessionState.Drive.Current.Name;
            }
            else
            {
                driveName = null;
            }
            
            do
            {
                if (path.Length == 0)
                {
                    // The current working directory is specified

                    result = false;
                    break;
                }

                if (path.StartsWith(@".\", StringComparison.Ordinal) ||
                    path.StartsWith(@"./", StringComparison.Ordinal))
                {
                    // The .\ prefix basically escapes anything that follows
                    // so treat it as a relative path no matter what comes
                    // after it.

                    result = false;
                    break;
                }

                int index = path.IndexOf(":", StringComparison.CurrentCulture);

                if (index == -1)
                {
                    // If there is no : then the path is relative to the
                    // current working drive

                    result = false;
                    break;
                }

                // If the : is the first character in the path then we
                // must assume that it is part of the path, and not
                // delimiting the drive name.

                if (index > 0)
                {
                    // We must have a drive specified
                    driveName = path.Substring(0, index);

                    result = true;
                }

            } while (false);

#if DEBUG
            if (result)
            {
                Dbg.Diagnostics.Assert(
                    driveName != null,
                    "The drive name should always have a value, " +
                    "the default is the current working drive");

                tracer.WriteLine(
                    "driveName = {0}",
                    driveName);
            }
#endif

            return result;

        } // IsAbsolutePath

        #endregion Path manipulation

        #endregion Public methods

        #region private fields and methods

        /// <summary>
        /// The instance of session state on which this globber acts.
        /// </summary>
        private SessionState sessionState;

        /// <summary>
        /// Removs the back tick "`" from any of the glob characters in the path.
        /// </summary>
        /// 
        /// <param name="path">
        /// The path to remove the glob escaping from.
        /// </param>
        /// 
        /// <returns>
        /// The path with the glob characters unescaped.
        /// </returns>
        /// 
        /// <exception cref="ArgumentNullException">
        /// If <paramref name="path" /> is null.
        /// </exception>
        /// 
        private static string RemoveGlobEscaping(string path)
        {
                if (path == null)
                {
                    throw PSTraceSource.NewArgumentNullException("path");
                }

                string result = WildcardPattern.Unescape(path);

                return result;
        } // RemoveGlobEscaping

        #region Path manipulation methods


        /// <summary>
        /// Determines if the given drive name is a "special" name defined
        /// by the shell. For instance, "default", "current", "global", and "scope[##]" are scopes
        /// for variables and are considered shell virtual drives.
        /// </summary>
        /// 
        /// <param name="driveName">
        /// The name of the drive to check to see if it is a shell virtual drive.
        /// </param>
        /// 
        /// <param name="scope">
        /// This out parameter is filled with the scope that the drive name represents.
        /// It will be null if the driveName does not represent a scope.
        /// </param>
        /// 
        /// <returns>
        /// true, if the drive name is a shell virtual drive like "Default" or "global",
        /// false otherwise.
        /// </returns>
        /// 
        /// <exception cref="ArgumentNullException">
        /// If <paramref name="driveName" /> is null.
        /// </exception>
        /// 
        /// <remarks>
        /// The comparison is done using a case-insensitive comparison using the
        /// Invariant culture.
        /// 
        /// This is internal so that it is accessible to SessionState.
        /// </remarks>
        /// 
        /// <exception cref="ArgumentNullException">
        /// If <paramref name="driveName"/> is null.
        /// </exception>
        /// 
        internal bool IsShellVirtualDrive(string driveName, out SessionStateScope scope)
        {
            if (driveName == null)
            {
                throw PSTraceSource.NewArgumentNullException("driveName");
            }

            bool result = false;

            // Is it the global scope?

            if (String.Compare(
                    driveName,
                    StringLiterals.Global,
                    StringComparison.OrdinalIgnoreCase) == 0)
            {
                tracer.WriteLine("match found: {0}", StringLiterals.Global);
                result = true;
                scope = sessionState.Internal.GlobalScope;
            } // globalScope

            // Is it the local scope?

            else if (String.Compare(
                        driveName,
                        StringLiterals.Local,
                        StringComparison.OrdinalIgnoreCase) == 0)
            {
                tracer.WriteLine("match found: {0}", driveName);
                result = true;
                scope = sessionState.Internal.CurrentScope;
            } // currentScope
            else
            {
                scope = null;
            }

            return result;
        } // IsShellVirtualDrive

        /// <summary>
        /// Gets a provider specific path that represents the specified path and is relative
        /// to the root of the PowerShell drive.
        /// </summary>
        /// 
        /// <param name="path">
        /// Can be a relative or absolute path.
        /// </param>
        /// 
        /// <param name="context">
        /// The context which the core command is running.
        /// </param>
        /// 
        /// <param name="escapeCurrentLocation">
        /// Escape the wildcards in the current location.  Use when this path will be
        /// passed through globbing.
        /// </param>
        /// 
        /// <param name="workingDriveForPath">
        /// This out parameter returns the drive that was specified
        /// by the <paramref name="path" />. If <paramref name="path"/> is
        /// an absolute path this value may be something other than
        /// the current working drive.
        /// 
        /// If the path refers to a non-existent drive, this parameter is set to null, and an exception is thrown.
        /// 
        /// </param>
        /// 
        /// <param name="providerInstance">
        /// The provider instance that was used.
        /// </param>
        /// 
        /// <returns>
        /// A  provider specific relative path to the root of the drive.
        /// </returns>
        /// 
        /// <remarks>
        /// The path is parsed to determine if it is a relative path to the
        /// current working drive or if it is an absolute path. If
        /// it is a relative path the provider specific path is generated using the current
        /// working directory, the drive root, and the path specified.
        /// If the path is an absolute path the provider specific path is generated by stripping
        /// of anything before the : and using that to find the appropriate
        /// drive. The provider specific path is then generated the same as the
        /// relative path using the specified drive instead of the
        /// current working drive.
        /// 
        /// This is internal so that it can be called from SessionState
        /// </remarks>
        /// 
        /// <exception cref="ArgumentNullException">
        /// If <paramref name="path" /> is null.
        /// </exception>
        /// 
        /// <exception cref="DriveNotFoundException">
        /// If the <paramref name="path"/> refers to a drive that could not be found.
        /// </exception>
        /// 
        /// <exception cref="ProviderInvocationException">
        /// If the provider specified by <paramref name="providerId"/> threw an
        /// exception when its GetParentPath or MakePath was called while
        /// processing the <paramref name="path"/>.
        /// </exception>
        /// 
        /// <exception cref="NotSupportedException">
        /// If the provider is not a NavigationCmdletProvider.
        /// </exception>
        /// 
        /// <exception cref="PipelineStoppedException">
        /// If <paramref name="context"/> has been signaled for
        /// StopProcessing.
        /// </exception>
        /// 
        internal string GetDriveRootRelativePathFromPSPath(
            string path,
            CmdletProviderContext context,
            bool escapeCurrentLocation,
            out PSDriveInfo workingDriveForPath,
            out CmdletProvider providerInstance)
        {
            // Verify parameters

            if (path == null)
            {
                throw PSTraceSource.NewArgumentNullException("path");
            }

            workingDriveForPath = null;
            string driveName = null;

            if (sessionState.Drive.Current != null)
            {
                driveName = sessionState.Drive.Current.Name;
            }

            // Check to see if the path is relative or absolute
            bool isPathForCurrentDrive = false;

            if (IsAbsolutePath(path, out driveName))
            {
                Dbg.Diagnostics.Assert(
                    driveName != null,
                    "IsAbsolutePath should be returning the drive name");

                tracer.WriteLine(
                    "Drive Name: {0}",
                    driveName);

                // This will resolve $GLOBAL, and $LOCAL as needed.
                // This throws DriveNotFoundException if a drive of the specified
                // name does not exist. Just let the exception propogate out.
                try
                {
                    workingDriveForPath = sessionState.Drive.Get(driveName);
                }
                catch(DriveNotFoundException)
                {
                    // Check to see if it is a path relative to the
                    // current drive's root. This is true when a drive root
                    // appears to be a drive (like HTTP://). The drive will not
                    // actually exist, but this is not an absolute path.

                    if (sessionState.Drive.Current == null)
                    {
                        throw;
                    }

                    string normalizedRoot = sessionState.Drive.Current.Root.Replace(
                        StringLiterals.AlternatePathSeparator, StringLiterals.DefaultPathSeparator);

                    if (normalizedRoot.IndexOf(":", StringComparison.CurrentCulture) >= 0)
                    {
                        string normalizedPath = path.Replace(StringLiterals.AlternatePathSeparator, StringLiterals.DefaultPathSeparator);
                        if (normalizedPath.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
                        {
                            isPathForCurrentDrive = true;
                            path = path.Substring(normalizedRoot.Length);
                            path = path.TrimStart(StringLiterals.DefaultPathSeparator);
                            path = StringLiterals.DefaultPathSeparator + path;
                            workingDriveForPath = sessionState.Drive.Current;
                        }
                    }

                    if (!isPathForCurrentDrive)
                    {
                        throw;
                    }
                }

                // Now hack off the drive component of the path
                if (!isPathForCurrentDrive)
                {
                    path = path.Substring(driveName.Length + 1);
                }
            }
            else
            {
                // it's a relative path, so the working drive is the current drive
                workingDriveForPath = sessionState.Drive.Current;
            }

            if (workingDriveForPath == null)
            {
                ItemNotFoundException pathNotFound =
                    new ItemNotFoundException(
                        path,
                        "PathNotFound",
                        SessionStateStrings.PathNotFound);

                pathResolutionTracer.TraceError("Item does not exist: {0}", path);

                throw pathNotFound;
            }

            try
            {
                providerInstance =
                    sessionState.Internal.GetContainerProviderInstance(workingDriveForPath.Provider);

                // Add the drive info to the context so that downstream methods
                // have access to it.
                context.Drive = workingDriveForPath;

                string relativePath =
                    GenerateRelativePath(
                        workingDriveForPath,
                        path,
                        escapeCurrentLocation,
                        providerInstance,
                        context);

                return relativePath;
            }
            catch (PSNotSupportedException)
            {
                // If it's really not a container provider, the relative path will
                // always be empty

                providerInstance = null;
                return "";
            }

        } // GetDriveRootRelativePathFromPSPath

        private string GetDriveRootRelativePathFromProviderPath(
            string providerPath,
            PSDriveInfo drive,
            CmdletProviderContext context
            )
        {
            string childPath = "";

            CmdletProvider providerInstance = 
                sessionState.Internal.GetContainerProviderInstance(drive.Provider);
            NavigationCmdletProvider navigationProvider = providerInstance as NavigationCmdletProvider;

            // Normalize the paths
            providerPath = providerPath.Replace(StringLiterals.AlternatePathSeparator, StringLiterals.DefaultPathSeparator);
            providerPath = providerPath.TrimEnd(StringLiterals.DefaultPathSeparator);
            string driveRoot = drive.Root.Replace(StringLiterals.AlternatePathSeparator, StringLiterals.DefaultPathSeparator);
            driveRoot = driveRoot.TrimEnd(StringLiterals.DefaultPathSeparator);


            // Keep on lopping off children until the the remaining path
            // is the drive root.
            while((! String.IsNullOrEmpty(providerPath)) &&
                (! providerPath.Equals(driveRoot, StringComparison.OrdinalIgnoreCase)))
            {
                if (!String.IsNullOrEmpty(childPath))
                {
                    childPath = sessionState.Internal.MakePath(
                        providerInstance,
                        navigationProvider.GetChildName(providerPath, context),
                        childPath,
                        context);
                }
                else
                {
                    childPath = navigationProvider.GetChildName(providerPath, context);
                }

                providerPath = sessionState.Internal.GetParentPath(
                    providerInstance,
                    providerPath,
                    drive.Root,
                    context);
            }

            return childPath;
        }

        /// <summary>
        /// Builds a provider specific path from the current working
        /// directory using the specified relative path
        /// </summary>
        /// 
        /// <param name="drive">
        /// The drive to generate the provider specific path from.
        /// </param>
        /// 
        /// <param name="path">
        /// The relative path to add to the absolute path in the  drive.
        /// </param>
        ///
        /// <param name="escapeCurrentLocation">
        /// Escape the wildcards in the current location.  Use when this path will be
        /// passed through globbing.
        /// </param>
        /// 
        /// <param name="providerInstance">
        /// An instance of the provider to use if MakePath or GetParentPath
        /// need to be called.
        /// </param>
        /// 
        /// <param name="context">
        /// The context which the core command is running.
        /// </param>
        /// 
        /// <returns>
        /// A string with the joined current working path and relative
        /// path.
        /// </returns>
        /// 
        /// <exception cref="ArgumentNullException">
        /// If <paramref name="path"/> or <paramref name="drive"/> is null.
        /// </exception>
        /// 
        /// <exception cref="ProviderInvocationException">
        /// If the provider specified by <paramref name="providerId"/> threw an
        /// exception when its GetParentPath or MakePath was called while
        /// processing the <paramref name="path"/>.
        /// </exception>
        /// 
        /// <exception cref="NotSupportedException">
        /// If the provider is not a NavigationCmdletProvider.
        /// </exception>
        /// 
        /// <exception cref="PipelineStoppedException">
        /// If <paramref name="context"/> has been signaled for
        /// StopProcessing.
        /// </exception>
        /// 
        internal string GenerateRelativePath(
            PSDriveInfo drive,
            string path,
            bool escapeCurrentLocation,
            CmdletProvider providerInstance,
            CmdletProviderContext context)
        {
            // Verify parameters

            if (path == null)
            {
                throw PSTraceSource.NewArgumentNullException("path");
            }

            if (drive == null)
            {
                throw PSTraceSource.NewArgumentNullException("drive");
            }

            // This string will be filled in with the
            // new root relative working directory as we process
            // the supplied path

            string driveRootRelativeWorkingPath = drive.CurrentLocation;
            if ((!String.IsNullOrEmpty(driveRootRelativeWorkingPath) &&
                (driveRootRelativeWorkingPath.StartsWith(drive.Root, StringComparison.Ordinal))))
            {
                driveRootRelativeWorkingPath = driveRootRelativeWorkingPath.Substring(drive.Root.Length);
            }

            if (escapeCurrentLocation) { driveRootRelativeWorkingPath = WildcardPattern.Escape(driveRootRelativeWorkingPath); }

            // These are static strings that we will parse and
            // interpret if they are leading the path. Otherwise
            // we will just pass them on to the provider.

            const char monadRelativePathSeparatorBackslash = '\\';
            const char monadRelativePathSeparatorForwardslash = '/';
            const string currentDirSymbol = ".";
            const string parentDirSymbol = "..";
            const int parentDirSymbolLength = 2;
            const string currentDirRelativeSymbolBackslash = ".\\";
            const string currentDirRelativeSymbolForwardslash = "./";

            // If the path starts with the "\" then it is 
            // relative to the root of the drive.
            // We don't want to process other relative path
            // symbols in this case

            if (String.IsNullOrEmpty(path))
            {
                // Just fall-through
            }
            else if (path[0] == monadRelativePathSeparatorBackslash ||
                     path[0] == monadRelativePathSeparatorForwardslash)
            {
                // The root relative path was given so empty the current working path.

                driveRootRelativeWorkingPath = String.Empty;

                // Remove the \ or / from the drive relative
                // path

                path = path.Substring(1);

                tracer.WriteLine(
                    "path = {0}",
                    path);
            }
            else
            {
                // Now process all other relative path symbols like
                // ".." and "."
                while ((path.Length > 0) && HasRelativePathTokens(path))
                {
                    if (context.Stopping)
                    {
                        throw new PipelineStoppedException();
                    }

                    bool processedSomething = false;

                    // Process the parent directory symbol ".."

                    bool pathStartsWithDirSymbol = path.StartsWith(parentDirSymbol, StringComparison.Ordinal);
                    bool pathLengthEqualsParentDirSymbol = path.Length == parentDirSymbolLength;
                    bool pathDirSymbolFollowedBySeparator =
                        (path.Length > parentDirSymbolLength) &&
                        ((path[parentDirSymbolLength] == monadRelativePathSeparatorBackslash) ||
                         (path[parentDirSymbolLength] == monadRelativePathSeparatorForwardslash));

                    if (pathStartsWithDirSymbol &&
                        (pathLengthEqualsParentDirSymbol ||
                         pathDirSymbolFollowedBySeparator))
                    {
                        if (!String.IsNullOrEmpty(driveRootRelativeWorkingPath))
                        {
                            // Use the provider to get the current path

                            driveRootRelativeWorkingPath =
                                sessionState.Internal.GetParentPath(
                                    providerInstance,
                                    driveRootRelativeWorkingPath,
                                    drive.Root,
                                    context);
                        }

                        tracer.WriteLine(
                            "Parent path = {0}",
                            driveRootRelativeWorkingPath);

                        // remove the parent path symbol from the 
                        // relative path

                        path =
                            path.Substring(
                            parentDirSymbolLength);

                        tracer.WriteLine(
                            "path = {0}",
                            path);

                        processedSomething = true;
                        if (path.Length == 0)
                        {
                            break;
                        }

                        // If the ".." was followed by a "\" or "/" then
                        // strip that off as well

                        if (path[0] == monadRelativePathSeparatorBackslash ||
                            path[0] == monadRelativePathSeparatorForwardslash)
                        {
                            path = path.Substring(1);
                        }

                        tracer.WriteLine(
                            "path = {0}",
                            path);

                        // no more relative path to work with so break

                        if (path.Length == 0)
                        {
                            break;
                        }

                        // continue the loop instead of trying to process
                        // ".\". This makes the code easier for ".\" by
                        // not having to check for ".."

                        continue;
                    }


                    // Process the current directory symbol "."

                    if (path.Equals(currentDirSymbol, StringComparison.OrdinalIgnoreCase))
                    {
                        processedSomething = true;
                        path = String.Empty;
                        break;

                    }

                    if (path.StartsWith(currentDirRelativeSymbolBackslash, StringComparison.Ordinal) ||
                        path.StartsWith(currentDirRelativeSymbolForwardslash, StringComparison.Ordinal))
                    {
                        path = path.Substring(currentDirRelativeSymbolBackslash.Length);
                        processedSomething = true;
                        tracer.WriteLine(
                            "path = {0}",
                            path);

                        if (path.Length == 0)
                        {
                            break;
                        }
                    }

                    // If there is no more path to work with break
                    // out of the loop

                    if (path.Length == 0)
                    {
                        break;
                    }

                    if (!processedSomething)
                    {
                        // Since that path wasn't modified, break
                        // the loop.
                        break;
                    }
                } // while
            } // if (path.StartsWith(@"\", StringComparison.CurrentCulture))

            // If more relative path remains add that to
            // the known absolute path

            if (!String.IsNullOrEmpty(path))
            {
                driveRootRelativeWorkingPath =
                    sessionState.Internal.MakePath(
                        providerInstance,
                        driveRootRelativeWorkingPath,
                        path,
                        context);
            }

            NavigationCmdletProvider navigationProvider = providerInstance as NavigationCmdletProvider;
            if (navigationProvider != null)
            {
                string rootedPath = sessionState.Internal.MakePath(context.Drive.Root, driveRootRelativeWorkingPath, context);
                string normalizedRelativePath = navigationProvider.ContractRelativePath(rootedPath, context.Drive.Root, false, context);

                if (!String.IsNullOrEmpty(normalizedRelativePath))
                {
                    if (normalizedRelativePath.StartsWith(context.Drive.Root, StringComparison.Ordinal))
                        driveRootRelativeWorkingPath = normalizedRelativePath.Substring(context.Drive.Root.Length);
                    else
                        driveRootRelativeWorkingPath = normalizedRelativePath;
                }
                else
                    driveRootRelativeWorkingPath = "";
            }

            tracer.WriteLine(
                "result = {0}",
                driveRootRelativeWorkingPath);

            return driveRootRelativeWorkingPath;
        } // GenerateRelativePath

        private bool HasRelativePathTokens(string path)
        {
            string comparePath = path.Replace('/', '\\');

            return (
                comparePath.Equals(".", StringComparison.OrdinalIgnoreCase) ||
                comparePath.Equals("..", StringComparison.OrdinalIgnoreCase) ||
                comparePath.Contains("\\.\\") ||
                comparePath.Contains("\\..\\") ||
                comparePath.EndsWith("\\..", StringComparison.OrdinalIgnoreCase) ||
                comparePath.EndsWith("\\.", StringComparison.OrdinalIgnoreCase) ||
                comparePath.StartsWith("..\\", StringComparison.OrdinalIgnoreCase) ||
                comparePath.StartsWith(".\\", StringComparison.OrdinalIgnoreCase) ||
                comparePath.StartsWith("~", StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Uses the drive and a relative working path to construct
        /// a string which has a fully qualified provider specific path
        /// </summary>
        /// 
        /// <param name="drive">
        /// The drive to use as the root of the path.
        /// </param>
        /// 
        /// <param name="workingPath">
        /// The relative working directory to the specified drive.
        /// </param>
        /// 
        /// <param name="context">
        /// The context which the core command is running.
        /// </param>
        /// 
        /// <returns>
        /// A string which is contains the fully qualified path in provider
        /// specific form.
        /// </returns>
        /// 
        /// <exception cref="ArgumentNullException">
        /// If <paramref name="drive"/> or <paramref name="workingPath"/> is null.
        /// </exception>
        /// 
        /// <exception cref="ProviderInvocationException">
        /// If the provider throws an exception when its MakePath gets
        /// called.
        /// </exception>
        /// 
        private string GetProviderSpecificPath(
            PSDriveInfo drive,
            string workingPath,
            CmdletProviderContext context)
        {
            if (drive == null)
            {
                throw PSTraceSource.NewArgumentNullException("drive");
            }

            if (workingPath == null)
            {
                throw PSTraceSource.NewArgumentNullException("workingPath");
            }

            // Trace the inputs

            drive.Trace();
            tracer.WriteLine(
                "workingPath = {0}",
                workingPath);

            string result = drive.Root;

            try
            {
                result =
                    sessionState.Internal.MakePath(
                        drive.Provider,
                        result,
                        workingPath,
                        context);
            }
            catch (NotSupportedException)
            {
                // This is valid if the provider doesn't support MakePath.  The
                // drive should be enough.
            }

            return result;
        } // GetProviderSpecificPath


        /// <summary>
        /// Parses the provider-qualified path into the provider name and
        /// the provider-internal path.
        /// </summary>
        /// 
        /// <param name="path">
        /// The provider-qualified path to parse.
        /// </param>
        /// 
        /// <param name="providerId">
        /// The name of the provider specified by the path is returned through
        /// this out parameter.
        /// </param>
        /// 
        /// <returns>
        /// The provider-internal path.
        /// </returns>
        /// 
        /// <exception cref="ArgumentNullException">
        /// If <paramref name="path"/> is null.
        /// </exception>
        ///
        /// <exception cref="ArgumentException">
        /// If <paramref name="path"/> is not in the correct format.
        /// </exception>
        /// 
        private static string ParseProviderPath(string path, out string providerId)
        {
            if (path == null)
            {
                throw PSTraceSource.NewArgumentNullException("path");
            }

            int providerIdSeparatorIndex = path.IndexOf(StringLiterals.ProviderPathSeparator, StringComparison.Ordinal);

            if (providerIdSeparatorIndex <= 0)
            {
                ArgumentException e =
                    PSTraceSource.NewArgumentException(
                        "path",
                        SessionStateStrings.NotProviderQualifiedPath);
                throw e;
            }

            providerId = path.Substring(0, providerIdSeparatorIndex);
            string result = path.Substring(providerIdSeparatorIndex + StringLiterals.ProviderPathSeparator.Length);

            return result;
        } // ParseProviderPath


        #endregion Path manipulation methods

        #endregion private fields and methods

        #region internal methods

        /// <summary>
        /// Given a provider specific path that contains glob characters, this method
        /// will perform the globbing using the specified provider and return the
        /// matching provider specific paths.
        /// </summary>
        /// 
        /// <param name="path">
        /// The path containing the glob characters to resolve.
        /// </param>
        /// 
        /// <param name="allowNonexistingPaths">
        /// If true, a ItemNotFoundException will not be thrown for non-existing
        /// paths. Instead an appropriate path will be returned as if it did exist.
        /// </param>
        /// 
        /// <param name="containerProvider">
        /// The provider that will be used to glob the <paramref name="path" />.
        /// </param>
        /// 
        /// <param name="context">
        /// The context under which the command is occurring.
        /// </param>
        /// 
        /// <returns>
        /// An array of provider specific paths that match the glob path and
        /// filter (if supplied via the context).
        /// </returns>
        /// 
        /// <remarks>
        /// This method is internal because we don't want to expose the
        /// provider instances outside the engine.
        /// </remarks>
        /// 
        /// <exception cref="ArgumentNullException">
        /// If <paramref name="path"/>, <paramref name="containerProvider"/>, or
        /// <paramref name="context"/> is null.
        /// </exception>
        /// 
        /// <exception cref="ProviderInvocationException">
        /// If the provider used to build the path threw an exception.
        /// </exception>
        /// 
        /// <exception cref="InvalidOperationException">
        /// If the <paramref name="path"/> starts with "~" and the home location is not set for 
        /// the provider.
        /// </exception>
        /// 
        /// <exception cref="PipelineStoppedException">
        /// If <paramref name="context"/> has been signaled for
        /// StopProcessing.
        /// </exception>
        /// 
        /// <exception>
        /// Any exception can be thrown by the provider that is called to build
        /// the provider path.
        /// </exception>
        /// 
        internal Collection<string> GetGlobbedProviderPathsFromProviderPath(
            string path,
            bool allowNonexistingPaths,
            ContainerCmdletProvider containerProvider,
            CmdletProviderContext context)
        {
            if (path == null)
            {
                throw PSTraceSource.NewArgumentNullException("path");
            }

            if (containerProvider == null)
            {
                throw PSTraceSource.NewArgumentNullException("containerProvider");
            }

            if (context == null)
            {
                throw PSTraceSource.NewArgumentNullException("context");
            }

            Collection<string> expandedPaths =
                ExpandGlobPath(
                    path,
                    allowNonexistingPaths,
                    containerProvider,
                    context);

            return expandedPaths;
        } // GetGlobbedProviderPathsFromProviderPath

        /// <summary>
        /// Determines if the specified path contains any globing characters. These
        /// characters are defined as '?' and '*'.
        /// </summary>
        /// 
        /// <param name="path">
        /// The path to search for globing characters.
        /// </param>
        /// 
        /// <returns>
        /// True if the path contains any of the globing characters, false otherwise.
        /// </returns>
        /// 
        /// <exception cref="ArgumentNullException">
        /// If <paramref name="path"/> is null.
        /// </exception>
        /// 
        internal static bool StringContainsGlobCharacters(string path)
        {
            if (path == null)
            {
                throw PSTraceSource.NewArgumentNullException("path");
            }

            return WildcardPattern.ContainsWildcardCharacters(path);
        } // StringContainsGlobCharacters

        /// <summary>
        /// Determines if the path and context are such that we need to run through
        /// the globbing algorithm.
        /// </summary>
        ///
        /// <param name="path">
        /// The path to check for glob characters.
        /// </param>
        ///
        /// <param name="context">
        /// The context to check for filter, include, or exclude expressions.
        /// </param>
        ///
        /// <returns>
        /// True if globbing should be performed (the path has glob characters, or the context
        /// has either a an include, or an exclude expression). False otherwise.
        /// </returns>
        ///
        static internal bool ShouldPerformGlobbing(string path, CmdletProviderContext context)
        {
            bool pathContainsGlobCharacters = false;

            if (path != null)
            {
                pathContainsGlobCharacters = StringContainsGlobCharacters(path);
            }

            bool contextContainsIncludeExclude = false;
            bool contextContainsNoGlob = false;

            if (context != null)
            {
                bool includePresent = context.Include != null && context.Include.Count > 0;
                pathResolutionTracer.WriteLine("INCLUDE filter present: {0}", includePresent);

                bool excludePresent = context.Exclude != null && context.Exclude.Count > 0;
                pathResolutionTracer.WriteLine("EXCLUDE filter present: {0}", excludePresent);

                contextContainsIncludeExclude = includePresent || excludePresent;

                contextContainsNoGlob = context.SuppressWildcardExpansion;
                pathResolutionTracer.WriteLine("NOGLOB parameter present: {0}", contextContainsNoGlob);
            }

            pathResolutionTracer.WriteLine("Path contains wildcard characters: {0}", pathContainsGlobCharacters);

            return (pathContainsGlobCharacters || contextContainsIncludeExclude) && (!contextContainsNoGlob);
        } // ShouldPerformGlobbing

        /// <summary>
        /// Generates an array of provider specific paths from the single provider specific
        /// path using globing rules.
        /// </summary>
        /// 
        /// <param name="path">
        /// A path that may or may not contain globing characters.
        /// </param>
        /// 
        /// <param name="allowNonexistingPaths">
        /// If true, a ItemNotFoundException will not be thrown for non-existing
        /// paths. Instead an appropriate path will be returned as if it did exist.
        /// </param>
        /// 
        /// <param name="drive">
        /// The drive that the path is relative to.
        /// </param>
        ///
        /// <param name="provider">
        /// The provider that implements the namespace for the path that we are globing over.
        /// </param>
        /// 
        /// <param name="context">
        /// The context the provider uses when performing the operation.
        /// </param>
        /// 
        /// <returns>
        /// An array of path strings that match the globing rules applied to the path parameter.
        /// </returns>
        /// 
        /// <remarks>
        /// First the path is checked to see if it contains any globing characters ('?' or '*').
        /// If it doesn't then the path is returned as the only element in the array.
        /// If it does, GetParentPath and GetLeafPathName is called on the path and each element
        /// is stored until the path doesn't contain any globing characters. At that point 
        /// GetChildNames() is called on the provider with the last parent path that doesn't
        /// contain a globing character. All the results are then matched against leaf element
        /// of that parent path (which did contain a glob character). We then walk out of the
        /// recursion and apply the same procedure to each leaf element that contained globing
        /// characters.
        /// 
        /// The procedure above allows us to match globing strings in multiple sub-containers
        /// in the namespace without having to have knowledge of the namespace paths, or
        /// their syntax.
        /// 
        /// Example:
        /// dir c:\foo\*\bar\*a??.cs
        /// 
        /// Calling this method for the path above would return all files that end in 'a' and
        /// any other two characters followed by ".cs" in all the subdirectories of
        /// foo that have a bar subdirectory.
        /// 
        /// </remarks>
        /// 
        /// <exception cref="ArgumentNullException">
        /// If <paramref name="path"/>, <paramref name="provider"/>, or
        /// <paramref name="provider"/> is null.
        /// </exception>
        /// 
        /// <exception cref="ProviderInvocationException">
        /// If the provider used to build the path threw an exception.
        /// </exception>
        /// 
        /// <exception cref="NotSupportedException">
        /// If the provider that the <paramref name="path"/> represents is not a NavigationCmdletProvider
        /// or ContainerCmdletProvider.
        /// </exception>
        /// 
        /// <exception cref="InvalidOperationException">
        /// If the <paramref name="path"/> starts with "~" and the home location is not set for 
        /// the provider.
        /// </exception>
        /// 
        /// <exception cref="ProviderInvocationException">
        /// If the provider associated with the <paramref name="path"/> threw an
        /// exception when its GetParentPath or MakePath was called while
        /// processing the <paramref name="path"/>.
        /// </exception>
        /// 
        /// <exception cref="ItemNotFoundException">
        /// If <paramref name="path"/> does not contain glob characters and
        /// could not be found.
        /// </exception>
        /// 
        /// <exception cref="PipelineStoppedException">
        /// If <paramref name="context"/> has been signaled for
        /// StopProcessing.
        /// </exception>
        /// 
        /// <exception>
        /// Any exception can be thrown by the provider that is called to build
        /// the provider path.
        /// </exception>
        /// 
        private Collection<string> ExpandMshGlobPath(
            string path,
            bool allowNonexistingPaths,
            PSDriveInfo drive,
            ContainerCmdletProvider provider,
            CmdletProviderContext context)
        {
            if (path == null)
            {
                throw PSTraceSource.NewArgumentNullException("path");
            }

            if (provider == null)
            {
                throw PSTraceSource.NewArgumentNullException("provider");
            }

            if (drive == null)
            {
                throw PSTraceSource.NewArgumentNullException("drive");
            }
            tracer.WriteLine("path = {0}", path);

            NavigationCmdletProvider navigationProvider = provider as NavigationCmdletProvider;

            Collection<string> result = new Collection<string>();

            using (pathResolutionTracer.TraceScope("EXPANDING WILDCARDS"))
            {
                if (ShouldPerformGlobbing(path, context))
                {
                    // This collection contains the directories for which a leaf is being added.
                    // If the directories are being globed over as well, then there will be
                    // many directories in this collection which will have to be iterated over
                    // every time there is a child being added

                    List<string> dirs = new List<string>();

                    // Each leaf element that is pulled off the path is pushed on the stack in
                    // order such that we can generate the path again.

                    Stack<String> leafElements = new Stack<String>();

                    using (pathResolutionTracer.TraceScope("Tokenizing path"))
                    {
                        // If the path contains glob characters then iterate through pulling the
                        // leaf elements off and pushing them on to the leafElements stack until
                        // there are no longer any glob characters in the path.

                        while (StringContainsGlobCharacters(path))
                        {
                            // Make sure to obey StopProcessing
                            if (context.Stopping)
                            {
                                throw new PipelineStoppedException();
                            }

                            // Use the provider to get the leaf element string

                            string leafElement = path;

                            if (navigationProvider != null)
                            {
                                leafElement = navigationProvider.GetChildName(path, context);
                            }

                            if (String.IsNullOrEmpty(leafElement))
                            {
                                break;
                            }

                            tracer.WriteLine("Pushing leaf element: {0}", leafElement);

                            pathResolutionTracer.WriteLine("Leaf element: {0}", leafElement);

                            // Push the leaf element onto the leaf element stack for future use

                            leafElements.Push(leafElement);

                            // Now use the parent path for the next iteration

                            if (navigationProvider != null)
                            {
                                // Now call GetParentPath with the root

                                string newParentPath = navigationProvider.GetParentPath(path, drive.Root, context);

                                if (String.Equals(
                                        newParentPath,
                                        path,
                                        StringComparison.OrdinalIgnoreCase))
                                {
                                    // The provider is implemented in an inconsistent way. 
                                    // GetChildName returned a non-empty/non-null result but
                                    // GetParentPath with the same path returns the same path.
                                    // This would cause the globber to go into an infinite loop,
                                    // so instead an exception is thrown.

                                    PSInvalidOperationException invalidOperation =
                                        PSTraceSource.NewInvalidOperationException(
                                            SessionStateStrings.ProviderImplementationInconsistent,
                                            provider.ProviderInfo.Name,
                                            path);
                                    throw invalidOperation;
                                }
                                path = newParentPath;
                            }
                            else
                            {
                                // If the provider doesn't implement NavigationCmdletProvider then at most
                                // it can have only one segment in its path. So after removing
                                // the leaf all we have left is the empty string.

                                path = String.Empty;
                            }

                            tracer.WriteLine("New path: {0}", path);

                            pathResolutionTracer.WriteLine("Parent path: {0}", path);
                        }

                        tracer.WriteLine("Base container path: {0}", path);

                        // If no glob elements were found there must be an include and/or
                        // exclude specified. Use the parent path to iterate over to 
                        // resolve the include/exclude filters

                        if (leafElements.Count == 0)
                        {
                            string leafElement = path;

                            if (navigationProvider != null)
                            {
                                leafElement = navigationProvider.GetChildName(path, context);

                                if (!String.IsNullOrEmpty(leafElement))
                                {
                                    path = navigationProvider.GetParentPath(path, null, context);
                                }
                            }
                            else
                            {
                                path = String.Empty;
                            }

                            leafElements.Push(leafElement);

                            pathResolutionTracer.WriteLine("Leaf element: {0}", leafElement);
                        }

                        pathResolutionTracer.WriteLine("Root path of resolution: {0}", path);
                    }

                    // Once the container path with no glob characters are found store it
                    // so that it's children can be iterated over.

                    dirs.Add(path);

                    // Reconstruct the path one leaf element at a time, expanding wherever
                    // we encounter glob characters

                    while (leafElements.Count > 0)
                    {
                        // Make sure to obey StopProcessing
                        if (context.Stopping)
                        {
                            throw new PipelineStoppedException();
                        }

                        string leafElement = leafElements.Pop();

                        Dbg.Diagnostics.Assert(
                            leafElement != null,
                            "I am only pushing strings onto this stack so I should be able " +
                            "to cast any Pop to a string without failure.");

                        dirs =
                            GenerateNewPSPathsWithGlobLeaf(
                                dirs,
                                drive,
                                leafElement,
                                leafElements.Count == 0,
                                provider,
                                context);

                        // If there are more leaf elements in the stack we need
                        // to make sure that only containers where added to dirs
                        // in GenerateNewPathsWithGlobLeaf

                        if (leafElements.Count > 0)
                        {
                            using (pathResolutionTracer.TraceScope("Checking matches to ensure they are containers"))
                            {
                                int index = 0;

                                while (index < dirs.Count)
                                {
                                    // Make sure to obey StopProcessing
                                    if (context.Stopping)
                                    {
                                        throw new PipelineStoppedException();
                                    }

                                    string resolvedPath =
                                        GetMshQualifiedPath(dirs[index], drive);

                                    // Check to see if the matching item is a container

                                    if (navigationProvider != null &&
                                        !sessionState.Internal.IsItemContainer(
                                            resolvedPath,
                                            context))
                                    {
                                        // If not, remove it from the collection

                                        tracer.WriteLine(
                                            "Removing {0} because it is not a container",
                                            dirs[index]);

                                        pathResolutionTracer.WriteLine("{0} is not a container", dirs[index]);
                                        dirs.RemoveAt(index);
                                    }
                                    else if (navigationProvider == null)
                                    {
                                        Dbg.Diagnostics.Assert(
                                            navigationProvider != null,
                                            "The path in the dirs should never be a container unless " +
                                            "the provider implements the NavigationCmdletProvider interface. If it " +
                                            "doesn't, there should be no more leafElements in the stack " +
                                            "when this check is done");
                                    }
                                    else
                                    {
                                        pathResolutionTracer.WriteLine("{0} is a container", dirs[index]);

                                        // If so, leave it and move on to the next one

                                        ++index;
                                    }
                                }
                            }
                        }
                    } // while (leafElements.Count > 0)

                    Dbg.Diagnostics.Assert(
                        dirs != null,
                        "GenerateNewPathsWithGlobLeaf() should return the base path as an element " +
                        "even if there are no globing characters");

                    foreach (string dir in dirs)
                    {
                        pathResolutionTracer.WriteLine("RESOLVED PATH: {0}", dir);
                        result.Add(dir);
                    }

                    Dbg.Diagnostics.Assert(
                        dirs.Count == result.Count,
                        "The result of copying the globed strings should be the same " +
                        "as from the collection");
                }
                else
                {
                    string unescapedPath = context.SuppressWildcardExpansion ? path : RemoveGlobEscaping(path);

                    string formatString = "{0}:" + StringLiterals.DefaultPathSeparator + "{1}";

                    // Check to see if its a hidden provider drive.
                    if (drive.Hidden)
                    {
                        if (IsProviderDirectPath(unescapedPath))
                        {
                            formatString = "{1}";
                        }
                        else
                        {
                            formatString = "{0}::{1}";
                        }
                    }
                    else
                    {
                        if (path.StartsWith(StringLiterals.DefaultPathSeparatorString, StringComparison.Ordinal))
                        {
                            formatString = "{0}:{1}";
                        }
                    }

                    string resolvedPath =
                        String.Format(
                            System.Globalization.CultureInfo.InvariantCulture,
                            formatString,
                            drive.Name,
                            unescapedPath);

                    // Since we didn't do globbing, be sure the path exists
                    if (allowNonexistingPaths || 
                        provider.ItemExists(GetProviderPath(resolvedPath, context), context))
                    {
                        pathResolutionTracer.WriteLine("RESOLVED PATH: {0}", resolvedPath);
                        result.Add(resolvedPath);
                    }
                    else
                    {
                        ItemNotFoundException pathNotFound =
                            new ItemNotFoundException(
                                resolvedPath,
                                "PathNotFound",
                                SessionStateStrings.PathNotFound);

                        pathResolutionTracer.TraceError("Item does not exist: {0}", path);

                        throw pathNotFound;
                    }
                }
            }

            Dbg.Diagnostics.Assert(
                result != null,
                "This method should at least return the path or more if it has glob characters");

            return result;
        } // ExpandMshGlobPath

        /// <summary>
        /// Gets either a drive-qualified or provider-qualified path based on the drive 
        /// information.
        /// </summary>
        ///
        /// <param name="path">
        /// The path to create a qualified path from.
        /// </param>
        ///
        /// <param name="drive">
        /// The drive used to qualify the path.
        /// </param>
        ///
        /// <returns>
        /// Either a drive-qualified or provider-qualified Msh path.
        /// </returns>
        ///
        /// <remarks>
        /// The drive's Hidden property is used to determine if the path returned
        /// should be provider (hidden=true) or drive (hidden=false) qualified.
        /// </remarks>
        ///
        /// <exception cref="ArgumentNullException">
        /// If <paramref name="path"/> or <paramref name="drive"/> is null.
        /// </exception>
        /// 
        internal static string GetMshQualifiedPath(string path, PSDriveInfo drive)
        {
            Dbg.Diagnostics.Assert(
                drive != null,
                "The caller should verify drive before calling this method");

            string result = null;

            if (drive.Hidden)
            {
                if (LocationGlobber.IsProviderDirectPath(path))
                {
                    result = path;
                }
                else
                {
                    result = GetProviderQualifiedPath(path, drive.Provider);
                }
            }
            else
            {
                result = GetDriveQualifiedPath(path, drive);
            }

            return result;
        } // GetMshQualifiedPath

        /// <summary>
        /// Removes the provider or drive qualifier from a Msh path.
        /// </summary>
        ///
        /// <param name="path">
        /// The path to remove the qualifier from.
        /// </param>
        ///
        /// <param name="drive">
        /// The drive information used to determine if a provider qualifier
        /// or drive qualifier should be removed from the path.
        /// </param>
        ///
        /// <returns>
        /// The path with the Msh qualifier removed.
        /// </returns>
        ///
        static internal string RemoveMshQualifier(string path, PSDriveInfo drive)
        {
            Dbg.Diagnostics.Assert(
                drive != null,
                "The caller should verify drive before calling this method");

            Dbg.Diagnostics.Assert(
                path != null,
                "The caller should verify path before calling this method");

            string result = null;

            if (drive.Hidden)
            {
                result = RemoveProviderQualifier(path);
            }
            else
            {
                result = RemoveDriveQualifier(path);
            }

            return result;
        } // RemoveMshQualifier

        /// <summary>
        /// Given an Msh relative or absolute path, returns a drive-qualified absolute path.
        /// No globbing or relative path character expansion is done.
        /// </summary>
        ///
        /// <param name="path">
        /// The path to get the drive qualified path from.
        /// </param>
        ///
        /// <param name="drive">
        /// The drive the path should be qualified with.
        /// </param>
        ///
        /// <returns>
        /// A drive-qualified absolute Msh path.
        /// </returns>
        ///
        /// <exception cref="ArgumentNullException">
        /// If <paramref name="path"/> or <paramref name="drive"/> is null.
        /// </exception>
        /// 
        internal static string GetDriveQualifiedPath(string path, PSDriveInfo drive)
        {
            if (path == null)
            {
                throw PSTraceSource.NewArgumentNullException("path");
            }


            if (drive == null)
            {
                throw PSTraceSource.NewArgumentNullException("drive");
            }

            string result = path;
            bool treatAsRelative = true;

            // Ensure the drive name is the same as the portion of the path before
            // :. If not add the drive name and colon as if it was a relative path

            int index = path.IndexOf(':');

            if (index != -1)
            {
                if (drive.Hidden)
                {
                    treatAsRelative = false;
                }
                else
                {
                    string possibleDriveName = path.Substring(0, index);
                    if (String.Equals(possibleDriveName, drive.Name, StringComparison.OrdinalIgnoreCase))
                    {
                        treatAsRelative = false;
                    }
                }
            }

            if (treatAsRelative)
            {
                string formatString = "{0}:" + StringLiterals.DefaultPathSeparator + "{1}";

                if (path.StartsWith(StringLiterals.DefaultPathSeparatorString, StringComparison.Ordinal))
                {
                    formatString = "{0}:{1}";
                }
                result =
                    String.Format(
                        System.Globalization.CultureInfo.InvariantCulture,
                        formatString,
                        drive.Name,
                        path);
            }

            return result;
        } // GetDriveQualifiedPath

        /// <summary>
        /// Removes the drive qualifier from a drive qualified MSH path
        /// </summary>
        ///
        /// <param name="path">
        /// The path to remove the drive qualifier from.
        /// </param>
        ///
        /// <returns>
        /// The path without the drive qualifier.
        /// </returns>
        ///
        private static string RemoveDriveQualifier(string path)
        {
            Dbg.Diagnostics.Assert(
                path != null,
                "Caller should verify path");

            string result = path;

            // Find the drive separator

            int index = path.IndexOf(":", StringComparison.Ordinal);

            if (index != -1)
            {
                // Remove the \ or / if it follows the drive indicator

                if (path[index + 1] == '\\' ||
                    path[index + 1] == '/')
                {
                    ++index;
                }

                result = path.Substring(index + 1);
            }

            return result;
        } // RemoveDriveQualifier

        /// <summary>
        /// Given an Msh path, returns a provider-qualified path.
        /// No globbing or relative path character expansion is done.
        /// </summary>
        ///
        /// <param name="path">
        /// The path to get the drive qualified path from.
        /// </param>
        ///
        /// <param name="provider">
        /// The provider the path should be qualified with.
        /// </param>
        ///
        /// <returns>
        /// A drive-qualified absolute Msh path.
        /// </returns>
        ///
        /// <exception cref="ArgumentNullException">
        /// If <paramref name="path"/> or <paramref name="provider"/> is null.
        /// </exception>
        /// 
        internal static string GetProviderQualifiedPath(string path, ProviderInfo provider)
        {
            if (path == null)
            {
                throw PSTraceSource.NewArgumentNullException("path");
            }


            if (provider == null)
            {
                throw PSTraceSource.NewArgumentNullException("provider");
            }

            string result = path;
            bool pathResolved = false;

            // Check to see if the path is already provider qualified

            int providerSeparatorIndex = path.IndexOf("::", StringComparison.Ordinal);
            if (providerSeparatorIndex != -1)
            {
                string possibleProvider = path.Substring(0, providerSeparatorIndex);

                if (provider.NameEquals(possibleProvider))
                {
                    pathResolved = true;
                }
            }

            if (!pathResolved)
            {
                result =
                    String.Format(
                        System.Globalization.CultureInfo.InvariantCulture,
                        "{0}{1}{2}",
                        provider.FullName,
                        StringLiterals.ProviderPathSeparator,
                        path);
            }

            return result;
        } // GetProviderQualifiedPath

        /// <summary>
        /// Removes the provider qualifier from a provider-qualified MSH path
        /// </summary>
        ///
        /// <param name="path">
        /// The path to remove the provider qualifier from.
        /// </param>
        ///
        /// <returns>
        /// The path without the provider qualifier.
        /// </returns>
        ///
        internal static string RemoveProviderQualifier(string path)
        {
            Dbg.Diagnostics.Assert(
                path != null,
                "Caller should verify path");

            string result = path;

            // Find the drive separator

            int index = path.IndexOf(StringLiterals.ProviderPathSeparator, StringComparison.Ordinal);

            if (index != -1)
            {
                // The +2 removes the ::
                result = path.Substring(index + StringLiterals.ProviderPathSeparator.Length);
            }

            return result;
        } // RemoveProviderQualifier

        /// <summary>
        /// Generates a collection of containers and/or leaves that are children of the containers
        /// in the currentDirs parameter and match the glob expression in the 
        /// <paramref name="leafElement" /> parameter.
        /// </summary>
        /// 
        /// <param name="currentDirs">
        /// A collection of paths that should be searched for leaves that match the 
        /// <paramref name="leafElement" /> expression.
        /// </param>
        /// 
        /// <param name="drive">
        /// The drive the Msh path is relative to.
        /// </param>
        ///
        /// <param name="leafElement">
        /// A single element of a path that may or may not contain a glob expression. This parameter
        /// is used to search the containers in <paramref name="currentDirs" /> for children that 
        /// match the glob expression.
        /// </param>
        /// 
        /// <param name="isLastLeaf">
        /// True if the <paramref name="leafElement" /> is the last element to glob over. If false, we 
        /// need to get all container names from the provider even if they don't match the filter.
        /// </param>
        /// 
        /// <param name="provider">
        /// The provider associated with the paths that are being passed in the 
        /// <paramref name="currentDirs" /> and <paramref name="leafElement" /> parameters.
        /// The provider must derive from ContainerCmdletProvider or NavigationCmdletProvider 
        /// in order to get globbing.
        /// </param>
        /// 
        /// <param name="context">
        /// The context the provider uses when performing the operation.
        /// </param>
        /// 
        /// <returns>
        /// A collection of fully qualified namespace paths whose leaf element matches the 
        /// <paramref name="leafElement" /> expression.
        /// </returns>
        /// 
        /// <exception cref="ArgumentNullException">
        /// If <paramref name="currentDirs" /> or <paramref name="provider" />
        /// is null.
        /// </exception>
        /// 
        /// <exception cref="ProviderInvocationException">
        /// If the provider used to build the path threw an exception.
        /// </exception>
        /// 
        /// <exception cref="NotSupportedException">
        /// If the provider that the <paramref name="path"/> represents is not a NavigationCmdletProvider
        /// or ContainerCmdletProvider.
        /// </exception>
        /// 
        /// <exception cref="InvalidOperationException">
        /// If the <paramref name="path"/> starts with "~" and the home location is not set for 
        /// the provider.
        /// </exception>
        /// 
        /// <exception cref="ProviderInvocationException">
        /// If the provider associated with the <paramref name="path"/> threw an
        /// exception when its GetParentPath or MakePath was called while
        /// processing the <paramref name="path"/>.
        /// </exception>
        /// 
        /// <exception cref="PipelineStoppedException">
        /// If <paramref name="context"/> has been signaled for
        /// StopProcessing.
        /// </exception>
        /// 
        /// <exception>
        /// Any exception can be thrown by the provider that is called to build
        /// the provider path.
        /// </exception>
        /// 
        private List<string> GenerateNewPSPathsWithGlobLeaf(
            List<string> currentDirs,
            PSDriveInfo drive,
            string leafElement,
            bool isLastLeaf,
            ContainerCmdletProvider provider,
            CmdletProviderContext context)
        {
            if (currentDirs == null)
            {
                throw PSTraceSource.NewArgumentNullException("currentDirs");
            }

            if (provider == null)
            {
                throw PSTraceSource.NewArgumentNullException("provider");
            }

            NavigationCmdletProvider navigationProvider = provider as NavigationCmdletProvider;

            List<string> newDirs = new List<string>();

            // Only loop through the child names if the leafElement contains a glob character

            if (!string.IsNullOrEmpty(leafElement) &&
                StringContainsGlobCharacters(leafElement) ||
                isLastLeaf)
            {
                string regexEscapedLeafElement = ConvertMshEscapeToRegexEscape(leafElement);

                // Construct the glob filter

                WildcardPattern stringMatcher =
                    WildcardPattern.Get(
                        regexEscapedLeafElement,
                        WildcardOptions.IgnoreCase);

                // Construct the include filter

                Collection<WildcardPattern> includeMatcher =
                    SessionStateUtilities.CreateWildcardsFromStrings(
                        context.Include,
                        WildcardOptions.IgnoreCase);


                // Construct the exclude filter

                Collection<WildcardPattern> excludeMatcher =
                    SessionStateUtilities.CreateWildcardsFromStrings(
                        context.Exclude,
                        WildcardOptions.IgnoreCase);

                // Loop through the current dirs and add the appropriate children

                foreach (string dir in currentDirs)
                {
                    using (pathResolutionTracer.TraceScope("Expanding wildcards for items under '{0}'", dir))
                    {
                        // Make sure to obey StopProcessing
                        if (context.Stopping)
                        {
                            throw new PipelineStoppedException();
                        }

                        // Now continue on with the names that were returned

                        string mshQualifiedParentPath = String.Empty;
                        Collection<PSObject> childNamesObjectArray =
                            GetChildNamesInDir(
                                dir,
                                leafElement,
                                !isLastLeaf,
                                context,
                                false,
                                drive,
                                provider,
                                out mshQualifiedParentPath);

                        if (childNamesObjectArray == null)
                        {
                            tracer.TraceError("GetChildNames returned a null array");
                            pathResolutionTracer.WriteLine("No child names returned for '{0}'", dir);
                            continue;
                        }


                        // Loop through each child to see if they match the glob expression

                        foreach (PSObject childObject in childNamesObjectArray)
                        {
                            // Make sure to obey StopProcessing
                            if (context.Stopping)
                            {
                                throw new PipelineStoppedException();
                            }

                            string child = String.Empty;

                            if (IsChildNameAMatch(
                                    childObject,
                                    stringMatcher,
                                    includeMatcher,
                                    excludeMatcher,
                                    out child))
                            {
                                string childPath = child;

                                if (navigationProvider != null)
                                {
                                    string parentPath = RemoveMshQualifier(mshQualifiedParentPath, drive);

                                    childPath =
                                        sessionState.Internal.
                                            MakePath(
                                                parentPath,
                                                child,
                                                context);

                                    childPath = GetMshQualifiedPath(childPath, drive);
                                }

                                tracer.WriteLine("Adding child path to dirs {0}", childPath);

                                // -- If there are more leafElements, the current childPath will be treated as a container path later,
                                //    we should escape the childPath in case the actual childPath contains wildcard characters such as '[' or ']'.
                                // -- If there is no more leafElement, the childPath will not be further processed, and we don't need to
                                //    escape it.
                                childPath = isLastLeaf ? childPath : WildcardPattern.Escape(childPath);
                                newDirs.Add(childPath);
                            }
                        } // foreach (child in childNames)
                    }
                } // foreach (dir in currentDirs)
            } // if (StringContainsGlobCharacters(leafElement))
            else
            {
                tracer.WriteLine(
                    "LeafElement does not contain any glob characters so do a MakePath");

                // Loop through the current dirs and add the leafElement to each of 
                // the dirs

                foreach (string dir in currentDirs)
                {
                    using (pathResolutionTracer.TraceScope("Expanding intermediate containers under '{0}'", dir))
                    {
                        // Make sure to obey StopProcessing
                        if (context.Stopping)
                        {
                            throw new PipelineStoppedException();
                        }

                        string backslashEscapedLeafElement = ConvertMshEscapeToRegexEscape(leafElement);

                        string unescapedDir = context.SuppressWildcardExpansion ? dir : RemoveGlobEscaping(dir);
                        string resolvedPath = GetMshQualifiedPath(unescapedDir, drive);

                        string childPath = backslashEscapedLeafElement;

                        if (navigationProvider != null)
                        {
                            string parentPath = RemoveMshQualifier(resolvedPath, drive);

                            childPath =
                                sessionState.Internal.
                                    MakePath(
                                        parentPath,
                                        backslashEscapedLeafElement,
                                        context);

                            childPath = GetMshQualifiedPath(childPath, drive);
                        }


                        if (sessionState.Internal.ItemExists(childPath, context))
                        {
                            tracer.WriteLine("Adding child path to dirs {0}", childPath);
                            pathResolutionTracer.WriteLine("Valid intermediate container: {0}", childPath);

                            newDirs.Add(childPath);
                        }
                    }
                } // foreach (dir in currentDirs)

            } // if (StringContainsGlobCharacters(leafElement))


            return newDirs;

        } // GenerateNewPSPathsWithGlobLeaf

        /// <summary>
        /// Generates an array of provider specific paths from the single provider specific
        /// path using globing rules.
        /// </summary>
        /// 
        /// <param name="path">
        /// A path that may or may not contain globing characters.
        /// </param>
        /// 
        /// <param name="allowNonexistingPaths">
        /// If true, a ItemNotFoundException will not be thrown for non-existing
        /// paths. Instead an appropriate path will be returned as if it did exist.
        /// </param>
        /// 
        /// <param name="provider">
        /// The provider that implements the namespace for the path that we are globing over.
        /// </param>
        /// 
        /// <param name="context">
        /// The context the provider uses when performing the operation.
        /// </param>
        /// 
        /// <returns>
        /// An array of path strings that match the globing rules applied to the path parameter.
        /// </returns>
        /// 
        /// <remarks>
        /// First the path is checked to see if it contains any globing characters ('?' or '*').
        /// If it doesn't then the path is returned as the only element in the array.
        /// If it does, GetParentPath and GetLeafPathName is called on the path and each element
        /// is stored until the path doesn't contain any globing characters. At that point 
        /// GetChildPathNames() is called on the provider with the last parent path that doesn't
        /// contain a globing character. All the results are then matched against leaf element
        /// of that parent path (which did contain a glob character). We then walk out of the
        /// recursion and apply the same procedure to each leaf element that contained globing
        /// characters.
        /// 
        /// The procedure above allows us to match globing strings in multiple sub-containers
        /// in the namespace without having to have knowledge of the namespace paths, or
        /// their syntax.
        /// 
        /// Example:
        /// dir c:\foo\*\bar\*a??.cs
        /// 
        /// Calling this method for the path above would return all files that end in 'a' and
        /// any other two characters followed by ".cs" in all the subdirectories of
        /// foo that have a bar subdirectory.
        /// 
        /// </remarks>
        /// 
        /// <exception cref="ArgumentNullException">
        /// If <paramref name="path"/> or <paramref name="provider"/> is null.
        /// </exception>
        /// 
        /// 
        /// <exception cref="ProviderInvocationException">
        /// If the provider used to build the path threw an exception.
        /// </exception>
        /// 
        /// <exception cref="InvalidOperationException">
        /// If the <paramref name="path"/> starts with "~" and the home location is not set for 
        /// the provider.
        /// or if the provider is implemented in such a way as to cause the globber to go
        /// into an infinite loop.
        /// </exception>
        /// 
        /// <exception cref="PipelineStoppedException">
        /// If <paramref name="context"/> has been signaled for
        /// StopProcessing.
        /// </exception>
        /// 
        /// <exception>
        /// Any exception can be thrown by the provider that is called to build
        /// the provider path.
        /// </exception>
        /// 
        internal Collection<string> ExpandGlobPath(
            string path,
            bool allowNonexistingPaths,
            ContainerCmdletProvider provider,
            CmdletProviderContext context)
        {
            if (path == null)
            {
                throw PSTraceSource.NewArgumentNullException("path");
            }

            if (provider == null)
            {
                throw PSTraceSource.NewArgumentNullException("provider");
            }

            // See if the provider wants to convert the path and filter
            string convertedPath = null;
            string convertedFilter = null;
            string originalFilter = context.Filter;
            bool changedPathOrFilter = provider.ConvertPath(path, context.Filter, ref convertedPath, ref convertedFilter, context);

            if (changedPathOrFilter)
            {
                if (tracer.IsEnabled)
                {
                    tracer.WriteLine("Provider converted path and filter.");
                    tracer.WriteLine("Original path: {0}", path);
                    tracer.WriteLine("Converted path: {0}", convertedPath);
                    tracer.WriteLine("Original filter: {0}", context.Filter);
                    tracer.WriteLine("Converted filter: {0}", convertedFilter);
                }

                path = convertedPath;
                originalFilter = context.Filter;
            }

            NavigationCmdletProvider navigationProvider = provider as NavigationCmdletProvider;

            tracer.WriteLine("path = {0}", path);

            Collection<string> result = new Collection<string>();

            using (pathResolutionTracer.TraceScope("EXPANDING WILDCARDS"))
            {
                if (ShouldPerformGlobbing(path, context))
                {
                    // This collection contains the directories for which a leaf is being added.
                    // If the directories are being globed over as well, then there will be
                    // many directories in this collection which will have to be iterated over
                    // every time there is a child being added

                    List<string> dirs = new List<string>();

                    // Each leaf element that is pulled off the path is pushed on the stack in
                    // order such that we can generate the path again.

                    Stack<String> leafElements = new Stack<String>();

                    using (pathResolutionTracer.TraceScope("Tokenizing path"))
                    {
                        // If the path contains glob characters then iterate through pulling the
                        // leaf elements off and pushing them on to the leafElements stack until
                        // there are no longer any glob characters in the path.

                        while (StringContainsGlobCharacters(path))
                        {
                            // Make sure to obey StopProcessing
                            if (context.Stopping)
                            {
                                throw new PipelineStoppedException();
                            }

                            // Use the provider to get the leaf element string

                            string leafElement = path;

                            if (navigationProvider != null)
                            {
                                leafElement = navigationProvider.GetChildName(path, context);
                            }

                            if (String.IsNullOrEmpty(leafElement))
                            {
                                break;
                            }

                            tracer.WriteLine("Pushing leaf element: {0}", leafElement);

                            pathResolutionTracer.WriteLine("Leaf element: {0}", leafElement);

                            // Push the leaf element onto the leaf element stack for future use

                            leafElements.Push(leafElement);

                            // Now use the parent path for the next iteration

                            if (navigationProvider != null)
                            {
                                // See if we can get the root from the context

                                string root = String.Empty;

                                if (context != null)
                                {
                                    PSDriveInfo drive = context.Drive;

                                    if (drive != null)
                                    {
                                        root = drive.Root;
                                    }
                                }

                                // Now call GetParentPath with the root

                                string newParentPath = navigationProvider.GetParentPath(path, root, context);

                                if (String.Equals(
                                        newParentPath,
                                        path,
                                        StringComparison.OrdinalIgnoreCase))
                                {
                                    // The provider is implemented in an inconsistent way. 
                                    // GetChildName returned a non-empty/non-null result but
                                    // GetParentPath with the same path returns the same path.
                                    // This would cause the globber to go into an infinite loop,
                                    // so instead an exception is thrown.

                                    PSInvalidOperationException invalidOperation =
                                        PSTraceSource.NewInvalidOperationException(
                                            SessionStateStrings.ProviderImplementationInconsistent,
                                            provider.ProviderInfo.Name,
                                            path);
                                    throw invalidOperation;
                                }
                                path = newParentPath;
                            }
                            else
                            {
                                // If the provider doesn't implement NavigationCmdletProvider then at most
                                // it can have only one segment in its path. So after removing
                                // the leaf all we have left is the empty string.

                                path = String.Empty;
                            }

                            tracer.WriteLine("New path: {0}", path);
                            pathResolutionTracer.WriteLine("Parent path: {0}", path);
                        }

                        tracer.WriteLine("Base container path: {0}", path);

                        // If no glob elements were found there must be an include and/or
                        // exclude specified. Use the parent path to iterate over to 
                        // resolve the include/exclude filters

                        if (leafElements.Count == 0)
                        {
                            string leafElement = path;

                            if (navigationProvider != null)
                            {
                                leafElement = navigationProvider.GetChildName(path, context);

                                if (!String.IsNullOrEmpty(leafElement))
                                {
                                    path = navigationProvider.GetParentPath(path, null, context);
                                }
                            }
                            else
                            {
                                path = String.Empty;
                            }
                            leafElements.Push(leafElement);
                            pathResolutionTracer.WriteLine("Leaf element: {0}", leafElement);
                        }

                        pathResolutionTracer.WriteLine("Root path of resolution: {0}", path);
                    }
                    // Once the container path with no glob characters are found store it
                    // so that it's children can be iterated over.

                    dirs.Add(path);

                    // Reconstruct the path one leaf element at a time, expanding where-ever
                    // we encounter glob characters

                    while (leafElements.Count > 0)
                    {
                        // Make sure to obey StopProcessing
                        if (context.Stopping)
                        {
                            throw new PipelineStoppedException();
                        }

                        string leafElement = leafElements.Pop();

                        Dbg.Diagnostics.Assert(
                            leafElement != null,
                            "I am only pushing strings onto this stack so I should be able " +
                            "to cast any Pop to a string without failure.");

                        dirs =
                            GenerateNewPathsWithGlobLeaf(
                                dirs,
                                leafElement,
                                leafElements.Count == 0,
                                provider,
                                context);

                        // If there are more leaf elements in the stack we need
                        // to make sure that only containers where added to dirs
                        // in GenerateNewPathsWithGlobLeaf

                        if (leafElements.Count > 0)
                        {
                            using (pathResolutionTracer.TraceScope("Checking matches to ensure they are containers"))
                            {
                                int index = 0;

                                while (index < dirs.Count)
                                {
                                    // Make sure to obey StopProcessing
                                    if (context.Stopping)
                                    {
                                        throw new PipelineStoppedException();
                                    }

                                    // Check to see if the matching item is a container

                                    if (navigationProvider != null &&
                                        !navigationProvider.IsItemContainer(
                                            dirs[index],
                                            context))
                                    {
                                        // If not, remove it from the collection

                                        tracer.WriteLine(
                                            "Removing {0} because it is not a container",
                                            dirs[index]);

                                        pathResolutionTracer.WriteLine("{0} is not a container", dirs[index]);
                                        dirs.RemoveAt(index);
                                    }
                                    else if (navigationProvider == null)
                                    {
                                        Dbg.Diagnostics.Assert(
                                            navigationProvider != null,
                                            "The path in the dirs should never be a container unless " +
                                            "the provider implements the NavigationCmdletProvider interface. If it " +
                                            "doesn't, there should be no more leafElements in the stack " +
                                            "when this check is done");
                                    }
                                    else
                                    {
                                        pathResolutionTracer.WriteLine("{0} is a container", dirs[index]);

                                        // If so, leave it and move on to the next one

                                        ++index;
                                    }
                                }
                            }
                        }
                    } // while (leafElements.Count > 0)

                    Dbg.Diagnostics.Assert(
                        dirs != null,
                        "GenerateNewPathsWithGlobLeaf() should return the base path as an element " +
                        "even if there are no globing characters");

                    foreach (string dir in dirs)
                    {
                        pathResolutionTracer.WriteLine("RESOLVED PATH: {0}", dir);
                        result.Add(dir);
                    }

                    Dbg.Diagnostics.Assert(
                        dirs.Count == result.Count,
                        "The result of copying the globed strings should be the same " +
                        "as from the collection");
                }
                else
                {
                    string unescapedPath = context.SuppressWildcardExpansion ? path : RemoveGlobEscaping(path);

                    if (allowNonexistingPaths ||
                        provider.ItemExists(unescapedPath, context))
                    {
                        pathResolutionTracer.WriteLine("RESOLVED PATH: {0}", unescapedPath);
                        result.Add(unescapedPath);
                    }
                    else
                    {
                        ItemNotFoundException pathNotFound =
                            new ItemNotFoundException(
                                path,
                                "PathNotFound",
                                SessionStateStrings.PathNotFound);

                        pathResolutionTracer.TraceError("Item does not exist: {0}", path);

                        throw pathNotFound;
                    }
                }
            }
            Dbg.Diagnostics.Assert(
                result != null,
                "This method should at least return the path or more if it has glob characters");

            if (changedPathOrFilter)
            {
                context.Filter = originalFilter;
            }

            return result;
        } // ExpandGlobPath


        /// <summary>
        /// Generates a collection of containers and/or leaves that are children of the containers
        /// in the currentDirs parameter and match the glob expression in the 
        /// <paramref name="leafElement" /> parameter.
        /// </summary>
        /// 
        /// <param name="currentDirs">
        /// A collection of paths that should be searched for leaves that match the 
        /// <paramref name="leafElement" /> expression.
        /// </param>
        /// 
        /// <param name="leafElement">
        /// A single element of a path that may or may not contain a glob expression. This parameter
        /// is used to search the containers in <paramref name="currentDirs" /> for children that 
        /// match the glob expression.
        /// </param>
        /// 
        /// <param name="isLastLeaf">
        /// True if the <paramref name="leafElement" /> is the last element to glob over. If false, we 
        /// need to get all container names from the provider even if they don't match the filter.
        /// </param>
        /// 
        /// <param name="provider">
        /// The provider associated with the paths that are being passed in the 
        /// <paramref name="currentDirs" /> and <paramref name="leafElement" /> parameters.
        /// The provider must derive from ContainerCmdletProvider or NavigationCmdletProvider 
        /// in order to get globbing.
        /// </param>
        /// 
        /// <param name="context">
        /// The context the provider uses when performing the operation.
        /// </param>
        /// 
        /// <returns>
        /// A collection of fully qualified namespace paths whose leaf element matches the 
        /// <paramref name="leafElement" /> expression.
        /// </returns>
        /// 
        /// <exception cref="ArgumentNullException">
        /// If <paramref name="currentDirs" /> or <paramref name="provider" />
        /// is null.
        /// </exception>
        /// 
        /// <exception cref="ProviderInvocationException">
        /// If the provider used to build the path threw an exception.
        /// </exception>
        /// 
        /// <exception cref="InvalidOperationException">
        /// If the <paramref name="path"/> starts with "~" and the home location is not set for 
        /// the provider.
        /// </exception>
        /// 
        /// <exception cref="PipelineStoppedException">
        /// If <paramref name="context"/> has been signaled for
        /// StopProcessing.
        /// </exception>
        /// 
        /// <exception>
        /// Any exception can be thrown by the provider that is called to build
        /// the provider path.
        /// </exception>
        /// 
        internal List<string> GenerateNewPathsWithGlobLeaf(
            List<string> currentDirs,
            string leafElement,
            bool isLastLeaf,
            ContainerCmdletProvider provider,
            CmdletProviderContext context)
        {
            if (currentDirs == null)
            {
                throw PSTraceSource.NewArgumentNullException("currentDirs");
            }

            if (provider == null)
            {
                throw PSTraceSource.NewArgumentNullException("provider");
            }

            NavigationCmdletProvider navigationProvider = provider as NavigationCmdletProvider;

            List<string> newDirs = new List<string>();

            // Only loop through the child names if the leafElement contains a glob character

            if (!string.IsNullOrEmpty(leafElement) &&
                (StringContainsGlobCharacters(leafElement) ||
                 isLastLeaf))
            {
                string regexEscapedLeafElement = ConvertMshEscapeToRegexEscape(leafElement);

                // Construct the glob filter

                WildcardPattern stringMatcher =
                    WildcardPattern.Get(
                        regexEscapedLeafElement,
                        WildcardOptions.IgnoreCase);

                // Construct the include filter

                Collection<WildcardPattern> includeMatcher =
                    SessionStateUtilities.CreateWildcardsFromStrings(
                        context.Include,
                        WildcardOptions.IgnoreCase);


                // Construct the exclude filter

                Collection<WildcardPattern> excludeMatcher =
                    SessionStateUtilities.CreateWildcardsFromStrings(
                        context.Exclude,
                        WildcardOptions.IgnoreCase);

                // Loop through the current dirs and add the appropriate children

                foreach (string dir in currentDirs)
                {
                    using (pathResolutionTracer.TraceScope("Expanding wildcards for items under '{0}'", dir))
                    {
                        // Make sure to obey StopProcessing
                        if (context.Stopping)
                        {
                            throw new PipelineStoppedException();
                        }

                        string unescapedDir = null;

                        Collection<PSObject> childNamesObjectArray =
                            GetChildNamesInDir(dir, leafElement, !isLastLeaf, context, true, null, provider, out unescapedDir);

                        if (childNamesObjectArray == null)
                        {
                            tracer.TraceError("GetChildNames returned a null array");

                            pathResolutionTracer.WriteLine("No child names returned for '{0}'", dir);
                            continue;
                        }

                        // Loop through each child to see if they match the glob expression

                        foreach (PSObject childObject in childNamesObjectArray)
                        {
                            // Make sure to obey StopProcessing
                            if (context.Stopping)
                            {
                                throw new PipelineStoppedException();
                            }

                            string child = String.Empty;
                            if (IsChildNameAMatch(childObject, stringMatcher, includeMatcher, excludeMatcher, out child))
                            {
                                string childPath = child;

                                if (navigationProvider != null)
                                {
                                    childPath =
                                        navigationProvider.
                                        MakePath(
                                            unescapedDir,
                                            child,
                                            context);
                                }

                                tracer.WriteLine("Adding child path to dirs {0}", childPath);

                                newDirs.Add(childPath);
                            }
                        } // foreach (child in childNames)
                    }
                } // foreach (dir in currentDirs)
            } // if (StringContainsGlobCharacters(leafElement))
            else
            {
                tracer.WriteLine(
                    "LeafElement does not contain any glob characters so do a MakePath");

                // Loop through the current dirs and add the leafElement to each of 
                // the dirs

                foreach (string dir in currentDirs)
                {
                    using (pathResolutionTracer.TraceScope("Expanding intermediate containers under '{0}'", dir))
                    {
                        // Make sure to obey StopProcessing
                        if (context.Stopping)
                        {
                            throw new PipelineStoppedException();
                        }

                        string backslashEscapedLeafElement = ConvertMshEscapeToRegexEscape(leafElement);

                        string unescapedDir = context.SuppressWildcardExpansion ? dir : RemoveGlobEscaping(dir);

                        string childPath = backslashEscapedLeafElement;

                        if (navigationProvider != null)
                        {
                            childPath =
                                navigationProvider.
                                    MakePath(
                                        unescapedDir,
                                        backslashEscapedLeafElement,
                                        context);
                        }


                        if (provider.ItemExists(childPath, context))
                        {
                            tracer.WriteLine("Adding child path to dirs {0}", childPath);

                            newDirs.Add(childPath);

                            pathResolutionTracer.WriteLine("Valid intermediate container: {0}", childPath);
                        }
                    }
                } // foreach (dir in currentDirs)
            } // if (StringContainsGlobCharacters(leafElement))


            return newDirs;

        } // GenerateNewPathsWithGlobLeaf

        /// <summary>
        /// Gets the child names in the specified path by using the provider
        /// </summary>
        /// 
        /// <param name="dir">
        /// The path of the directory to get the child names from. If this is an Msh Path,
        /// dirIsProviderPath must be false, If this is a provider-internal path,
        /// dirIsProviderPath must be true.
        /// </param>
        /// 
        /// <param name="leafElement">
        /// The element that we are ultimately looking for. Used to set filters on the context
        /// if desired.
        /// </param>
        /// 
        /// <param name="getAllContainers">
        /// Determines if the GetChildNames call should get all containers even if they don't
        /// match the filter.
        /// </param>
        /// 
        /// <param name="context">
        /// The context to be used for the command. The context is copied to a new context, the
        /// results are accumulated and then returned.
        /// </param>
        /// 
        /// <param name="dirIsProviderPath">
        /// Specifies whether the dir parameter is a provider-internal path (true) or Msh Path (false).
        /// </param>
        /// 
        /// <param name="drive">
        /// The drive to use to qualify the Msh path if dirIsProviderPath is false.
        /// </param>
        /// 
        /// <param name="provider">
        /// The provider to use to get the child names.
        /// </param>
        /// 
        /// <param name="modifiedDirPath">
        /// Returns the modified dir path. If dirIsProviderPath is true, this is the unescaped dir path.
        /// If dirIsProviderPath is false, this is the unescaped resolved provider path.
        /// </param>
        /// 
        /// <returns>
        /// A collection of PSObjects whose BaseObject is a string that contains the name of the child.
        /// </returns>
        /// 
        /// <exception cref="ArgumentNullException">
        /// If <paramref name="dir"/> or <paramref name="drive"/> is null.
        /// </exception>
        /// 
        /// <exception cref="ProviderNotFoundException">
        /// If the path is a provider-qualified path for a provider that is
        /// not loaded into the system.
        /// </exception>
        /// 
        /// <exception cref="DriveNotFoundException">
        /// If the <paramref name="path"/> refers to a drive that could not be found.
        /// </exception>
        /// 
        /// <exception cref="ProviderInvocationException">
        /// If the provider used to build the path threw an exception.
        /// </exception>
        /// 
        /// <exception cref="NotSupportedException">
        /// If the provider that the <paramref name="path"/> represents is not a NavigationCmdletProvider
        /// or ContainerCmdletProvider.
        /// </exception>
        /// 
        /// <exception cref="InvalidOperationException">
        /// If the <paramref name="path"/> starts with "~" and the home location is not set for 
        /// the provider.
        /// </exception>
        /// 
        /// <exception cref="ProviderInvocationException">
        /// If the provider associated with the <paramref name="path"/> threw an
        /// exception when its GetParentPath or MakePath was called while
        /// processing the <paramref name="path"/>.
        /// </exception>
        /// 
        /// <exception cref="PipelineStoppedException">
        /// If <paramref name="context"/> has been signaled for
        /// StopProcessing.
        /// </exception>
        /// 
        /// <exception>
        /// Any exception can be thrown by the provider that is called to build
        /// the provider path.
        /// </exception>
        /// 
        private Collection<PSObject> GetChildNamesInDir(
            string dir,
            string leafElement,
            bool getAllContainers,
            CmdletProviderContext context,
            bool dirIsProviderPath,
            PSDriveInfo drive,
            ContainerCmdletProvider provider,
            out string modifiedDirPath)
        {
            // See if the provider wants to convert the path and filter
            string convertedPath = null;
            string convertedFilter = null;
            string originalFilter = context.Filter;
            bool changedPathOrFilter = provider.ConvertPath(leafElement, context.Filter, ref convertedPath, ref convertedFilter, context);

            if (changedPathOrFilter)
            {
                if (tracer.IsEnabled)
                {
                    tracer.WriteLine("Provider converted path and filter.");
                    tracer.WriteLine("Original path: {0}", leafElement);
                    tracer.WriteLine("Converted path: {0}", convertedPath);
                    tracer.WriteLine("Original filter: {0}", context.Filter);
                    tracer.WriteLine("Converted filter: {0}", convertedFilter);
                }

                leafElement = convertedPath;
                context.Filter = convertedFilter;
            }

            ReturnContainers returnContainers = ReturnContainers.ReturnAllContainers;
            if (!getAllContainers)
            {
                returnContainers = ReturnContainers.ReturnMatchingContainers;
            }

            CmdletProviderContext getChildNamesContext =
                new CmdletProviderContext(context);

            // Remove the include/exclude filters from the new context
            getChildNamesContext.SetFilters(
                new Collection<string>(),
                new Collection<string>(),
                context.Filter);

            try
            {
                // Use the provider to get the children
                string unescapedDir = null;
                modifiedDirPath = null;

                if (dirIsProviderPath)
                {
                    modifiedDirPath = unescapedDir = context.SuppressWildcardExpansion ? dir : RemoveGlobEscaping(dir);
                }
                else
                {
                    Dbg.Diagnostics.Assert(
                        drive != null,
                        "Caller should verify that drive is not null when dirIsProviderPath is false");

                    // If the directory is an MSH path we must resolve it before calling GetChildNames()
                    // -- If the path is passed in by LiteralPath (context.SuppressWildcardExpansion == false), we surely should use 'dir' unchanged.
                    // -- If the path is passed in by Path (context.SuppressWildcardExpansion == true), we still should use 'dir' unchanged, in case that the special character
                    //    in 'dir' is escaped
                    modifiedDirPath = GetMshQualifiedPath(dir, drive);

                    ProviderInfo providerIgnored = null;
                    CmdletProvider providerInstanceIgnored = null;
                    Collection<string> resolvedPaths =
                        GetGlobbedProviderPathsFromMonadPath(
                            modifiedDirPath,
                            false,
                            getChildNamesContext,
                            out providerIgnored,
                            out providerInstanceIgnored);

                    // After resolving the path, we unescape the modifiedDirPath if necessary.
                    modifiedDirPath = context.SuppressWildcardExpansion
                                          ? modifiedDirPath
                                          : RemoveGlobEscaping(modifiedDirPath);
                    if (resolvedPaths.Count > 0)
                    {
                        unescapedDir = resolvedPaths[0];
                    }
                    else
                    {

                        // If there were no results from globbing but no
                        // exception was thrown, that means there was filtering.
                        // So return an empty collection and let the caller deal
                        // with it.

                        if (changedPathOrFilter)
                        {
                            context.Filter = originalFilter;
                        }

                        return new Collection<PSObject>();
                    }
                }

                if (provider.HasChildItems(unescapedDir, getChildNamesContext))
                {
                    provider.GetChildNames(
                        unescapedDir,
                        returnContainers,
                        getChildNamesContext);
                }

                // First check to see if there were any errors, and write them
                // to the real context if there are.

                if (getChildNamesContext.HasErrors())
                {
                    Collection<ErrorRecord> errors = getChildNamesContext.GetAccumulatedErrorObjects();

                    if (errors != null &&
                        errors.Count > 0)
                    {
                        foreach (ErrorRecord errorRecord in errors)
                        {
                            context.WriteError(errorRecord);
                        }
                    }
                }

                Collection<PSObject> childNamesObjectArray = getChildNamesContext.GetAccumulatedObjects();

                if (changedPathOrFilter)
                {
                    context.Filter = originalFilter;
                }

                return childNamesObjectArray;
            }
            finally
            {
                getChildNamesContext.RemoveStopReferral();
            }
        } // GetChildNamesInDir

        /// <summary>
        /// Determines if the specified PSObject contains a string that matches the specified 
        /// wildcard patterns. 
        /// </summary>
        /// 
        /// <param name="childObject">
        /// The PSObject that contains the child names.
        /// </param>
        /// 
        /// <param name="stringMatcher">
        /// The glob matcher.
        /// </param>
        /// 
        /// <param name="includeMatcher">
        /// The include matcher wildcard patterns.
        /// </param>
        /// 
        /// <param name="excludeMatcher">
        /// The exclude matcher wildcard patterns.
        /// </param>
        /// 
        /// <param name="childName">
        /// The name of the child which was extracted from the childObject and used for the matches.
        /// </param>
        /// 
        /// <returns>
        /// True if the string in the childObject matches the stringMatcher and includeMatcher wildcard patterns,
        /// and does not match the exclude wildcard patterns. False otherwise.
        /// </returns>
        /// 
        private static bool IsChildNameAMatch(
            PSObject childObject,
            WildcardPattern stringMatcher,
            Collection<WildcardPattern> includeMatcher,
            Collection<WildcardPattern> excludeMatcher,
            out string childName)
        {
            bool result = false;

            do // false loop
            {
                childName = null;
                object baseObject = childObject.BaseObject;
                if (baseObject is PSCustomObject)
                {
                    tracer.TraceError("GetChildNames returned a null object");
                    break;
                }

                childName = baseObject as string;

                if (childName == null)
                {
                    tracer.TraceError("GetChildNames returned an object that wasn't a string");
                    break;
                }

                pathResolutionTracer.WriteLine("Name returned from provider: {0}", childName);

                // Check the glob expression

                // First see if the child matches the glob expression
                bool isGlobbed = WildcardPattern.ContainsWildcardCharacters(stringMatcher.Pattern);
                bool isChildMatch = stringMatcher.IsMatch(childName);
                tracer.WriteLine("isChildMatch = {0}", isChildMatch);

                bool isIncludeSpecified = (includeMatcher.Count > 0);
                bool isExcludeSpecified = (excludeMatcher.Count > 0);
                bool isIncludeMatch =
                     SessionStateUtilities.MatchesAnyWildcardPattern(
                        childName,
                        includeMatcher,
                        true);

                tracer.WriteLine("isIncludeMatch = {0}", isIncludeMatch);

                // Check if the child name matches, or the include matches
                if (isChildMatch || (isGlobbed && isIncludeSpecified && isIncludeMatch))
                {
                    pathResolutionTracer.WriteLine("Path wildcard match: {0}", childName);
                    result = true;

                    // See if it should not be included
                    if (isIncludeSpecified && ! isIncludeMatch)
                    {
                        pathResolutionTracer.WriteLine("Not included match: {0}", childName);
                        result = false;
                    }

                    // See if it should be excluded
                    if (isExcludeSpecified &&
                        SessionStateUtilities.MatchesAnyWildcardPattern(childName, excludeMatcher, false))
                    {
                        pathResolutionTracer.WriteLine("Excluded match: {0}", childName);
                        result = false;
                    }
                }
                else
                {
                    pathResolutionTracer.WriteLine("NOT path wildcard match: {0}", childName);
                }
            } while (false);

            tracer.WriteLine("result = {0}; childName = {1}", result, childName);
            return result;
        } //IsChildNameAMatch

        /// <summary>
        /// Converts a back tick '`' escape into back slash escape for
        /// all occurrences in the string.
        /// </summary>
        /// 
        /// <param name="path">
        /// A string that may or may not have back ticks as escape characters.
        /// </param>
        /// 
        /// <returns>
        /// A string that has the back ticks replaced with back slashes except
        /// in the case where there are two back ticks in a row. In that case a single
        /// back tick is returned.
        /// </returns>
        /// 
        /// <remarks>
        /// The following rules apply to the conversion:
        /// 1. All \ characters are expanded to be \\
        /// 2. Any ` not followed by a ` is converted to a \
        /// 3. Any ` that is followed by a ` collapses the two into a single `
        /// 4. Any other character is immediately appended to the result.
        /// 
        /// </remarks>
        /// 
        /// <exception cref="ArgumentNullException">
        /// If <paramref name="path"/> is null.
        /// </exception>
        /// 
        private static string ConvertMshEscapeToRegexEscape(string path)
        {
            if (path == null)
            {
                throw PSTraceSource.NewArgumentNullException("path");
            }

            const char mshEscapeChar = '`';
            const char regexEscapeChar = '\\';

            char[] workerArray = path.ToCharArray();

            StringBuilder result = new StringBuilder();

            for (int index = 0; index < workerArray.GetLength(0); ++index)
            {
                // look for an escape character

                if (workerArray[index] == mshEscapeChar)
                {
                    if (index + 1 < workerArray.GetLength(0))
                    {
                        if (workerArray[index + 1] == mshEscapeChar)
                        {
                            // Since there are two escape characters in a row,
                            // the string really wanted a back tick so add that to
                            // the result and continue.

                            result.Append(mshEscapeChar);

                            // Skip the next character since it has already been processed.

                            ++index;
                        }
                        else
                        {
                            // Since the escape character wasn't followed by another
                            // escape character, convert it to a back slash and continue.

                            result.Append(regexEscapeChar);
                        }
                    }
                    else
                    {
                        // Since the escape character was the last character in the string
                        // just convert it. Most likely this is an error condition in the
                        // Regex class but I will let that fail instead of pretending to
                        // know what the user meant.

                        result.Append(regexEscapeChar);
                    }
                }
                else if (workerArray[index] == regexEscapeChar)
                {
                    // For backslashes we need to append two back slashes so that
                    // the regex processor doesn't think its an escape character

                    result.Append("\\\\");
                }
                else
                {
                    // The character is not an escape character so add it to the result
                    // and continue.

                    result.Append(workerArray[index]);
                }
            } // for ()

            tracer.WriteLine(
                "Original path: {0} Converted to: {1}",
                path,
                result.ToString());

            return result.ToString();

        } // ConvertMshEscapeToRegexEscape

        /// <summary>
        /// Determines if the path is relative to a provider home based on 
        /// the ~ character.
        /// </summary>
        ///
        /// <param name="path">
        /// The path to determine if it is a home path.
        /// </param>
        ///
        /// <returns>
        /// True if the path contains a ~ at the beginning of the path or immediately
        /// following a provider designator ("provider::")
        /// </returns>
        ///
        /// <exception cref="ArgumentNullException">
        /// Is <paramref name="path"/> is null.
        /// </exception>
        /// 
        internal static bool IsHomePath(string path)
        {
            if (path == null)
            {
                throw PSTraceSource.NewArgumentNullException("path");
            }

            bool result = false;

            if (IsProviderQualifiedPath(path))
            {
                // Strip off the provider portion of the path

                int index = path.IndexOf(StringLiterals.ProviderPathSeparator, StringComparison.Ordinal);

                if (index != -1)
                {
                    path = path.Substring(index + StringLiterals.ProviderPathSeparator.Length);
                }
            }

            if (path.IndexOf(StringLiterals.HomePath, StringComparison.Ordinal) == 0)
            {
                // Support the single "~"
                if (path.Length == 1)
                    result = true;
                // Support "~/" or "~\"
                else if ((path.Length > 1) &&
                        (path[1] == '\\' ||
                         path[1] == '/'))
                    result = true;
            }

            return result;
        } // IsHomePath

        /// <summary>
        /// Determines if the specified path looks like a remote path. (starts with
        /// // or \\.
        /// </summary>
        /// 
        /// <param name="path">
        /// The path to check to determine if it is a remote path.
        /// </param>
        /// 
        /// <returns>
        /// True if the path starts with // or \\, or false otherwise.
        /// </returns>
        /// 
        /// <exception cref="ArgumentNullException">
        /// If <paramref name="path"/> is null.
        /// </exception>
        /// 
        internal static bool IsProviderDirectPath(string path)
        {
            if (path == null)
            {
                throw PSTraceSource.NewArgumentNullException("path");
            }

            return path.StartsWith(StringLiterals.DefaultRemotePathPrefix, StringComparison.Ordinal) ||
                   path.StartsWith(StringLiterals.AlternateRemotePathPrefix, StringComparison.Ordinal);
        } // IsRemotePath

        /// <summary>
        /// Generates the path for the home location for a provider when given a
        /// path starting with ~ or "provider:~" followed by a relative path.
        /// </summary>
        ///
        /// <param name="path">
        /// The path to generate into a home path.
        /// </param>
        ///
        /// <returns>
        /// The path representing the path to the home location for a provider. This
        /// may be either a fully qualified provider path or a PowerShell path.
        /// </returns>
        ///
        /// <exception cref="ArgumentNullException">
        /// If <paramref name="path"/> is null.
        /// </exception>
        /// 
        /// <exception cref="ProviderNotFoundException">
        /// If <paramref name="path"/> is a provider-qualified path 
        /// and the specified provider does not exist.
        /// </exception>
        /// 
        /// <exception cref="ProviderInvocationException">
        /// If the provider throws an exception when its MakePath gets
        /// called.
        /// </exception>
        /// 
        /// <exception cref="NotSupportedException">
        /// If the provider does not support multiple items.
        /// </exception>
        /// 
        /// <exception cref="InvalidOperationException">
        /// If the home location for the provider is not set and
        /// <paramref name="path"/> starts with a "~".
        /// </exception>
        /// 
        internal string GetHomeRelativePath(string path)
        {
            if (path == null)
            {
                throw PSTraceSource.NewArgumentNullException("path");
            }

            string result = path;

            if (IsHomePath(path) && sessionState.Drive.Current != null)
            {
                ProviderInfo provider = sessionState.Drive.Current.Provider;

                if (IsProviderQualifiedPath(path))
                {
                    // Strip off the provider portion of the path

                    int index = path.IndexOf(StringLiterals.ProviderPathSeparator, StringComparison.Ordinal);

                    if (index != -1)
                    {
                        // Since the provider was specified store it and remove it
                        // from the path.

                        string providerName = path.Substring(0, index);

                        provider = sessionState.Internal.GetSingleProvider(providerName);
                        path = path.Substring(index + StringLiterals.ProviderPathSeparator.Length);
                    }
                } // IsProviderPath

                if (path.IndexOf(StringLiterals.HomePath, StringComparison.Ordinal) == 0)
                {
                    // Strip of the ~ and the \ or / if present

                    if (path.Length > 1 &&
                        (path[1] == '\\' ||
                         path[1] == '/'))
                    {
                        path = path.Substring(2);
                    }
                    else
                    {
                        path = path.Substring(1);
                    }

                    // Now piece together the provider's home path and the remaining
                    // portion of the passed in path

                    if (provider.Home != null &&
                        provider.Home.Length > 0)
                    {
                        CmdletProviderContext context =
                            new CmdletProviderContext(sessionState.Internal.ExecutionContext);

                        pathResolutionTracer.WriteLine("Getting home path for provider: {0}", provider.Name);
                        pathResolutionTracer.WriteLine("Provider HOME path: {0}", provider.Home);

                        if (String.IsNullOrEmpty(path))
                        {
                            path = provider.Home;
                        }
                        else
                        {
                            path = sessionState.Internal.MakePath(provider, provider.Home, path, context);
                        }

                        pathResolutionTracer.WriteLine("HOME relative path: {0}", path);
                    }
                    else
                    {
                        InvalidOperationException e =
                            PSTraceSource.NewInvalidOperationException(
                                SessionStateStrings.HomePathNotSet,
                                provider.Name);

                        pathResolutionTracer.TraceError("HOME path not set for provider: {0}", provider.Name);
                        throw e;
                    }
                }
                result = path;
            } // IsHomePath

            return result;
        } // GetHomeRelativePath

        private static void TraceFilters(CmdletProviderContext context)
        {
            if ((pathResolutionTracer.Options & PSTraceSourceOptions.WriteLine) != 0)
            {
                // Trace the filter
                pathResolutionTracer.WriteLine("Filter: {0}", context.Filter ?? String.Empty);

                if (context.Include != null)
                {
                    // Trace the include filters
                    StringBuilder includeString = new StringBuilder();
                    foreach (string includeFilter in context.Include)
                    {
                        includeString.AppendFormat("{0} ", includeFilter);
                    }
                    pathResolutionTracer.WriteLine("Include: {0}", includeString.ToString());
                }

                if (context.Exclude != null)
                {
                    // Trace the exclude filters
                    StringBuilder excludeString = new StringBuilder();
                    foreach (string excludeFilter in context.Exclude)
                    {
                        excludeString.AppendFormat("{0} ", excludeFilter);
                    }
                    pathResolutionTracer.WriteLine("Exclude: {0}", excludeString.ToString());
                }
            }
        }
        #endregion internal methods

    } // class LocationGlobber
} // namespace System.Management.Automation