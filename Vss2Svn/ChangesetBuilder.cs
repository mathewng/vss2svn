/* Copyright 2009 HPDI, LLC
 * 
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 * 
 *     http://www.apache.org/licenses/LICENSE-2.0
 * 
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using Hpdi.VssLogicalLib;

namespace Hpdi.Vss2Svn
{
    /// <summary>
    /// Reconstructs changesets from independent revisions.
    /// </summary>
    /// <author>Trevor Robinson</author>
    class ChangesetBuilder : Worker
    {
        private readonly RevisionAnalyzer revisionAnalyzer;

        private readonly LinkedList<Changeset> changesets = new LinkedList<Changeset>();
        public LinkedList<Changeset> Changesets
        {
            get { return changesets; }
        }

        private TimeSpan anyCommentThreshold = TimeSpan.FromSeconds(0);
        public TimeSpan AnyCommentThreshold
        {
            get { return anyCommentThreshold; }
            set { anyCommentThreshold = value; }
        }

        private TimeSpan emptyCommentThreshold = TimeSpan.FromSeconds(30);
        public TimeSpan EmptyCommentThreshold
        {
            get { return emptyCommentThreshold; }
            set { emptyCommentThreshold = value; }
        }

        private TimeSpan sameCommentThreshold = TimeSpan.FromMinutes(10);
        public TimeSpan SameCommentThreshold
        {
            get { return sameCommentThreshold; }
            set { sameCommentThreshold = value; }
        }

        private bool excludeAllDestroyedItems = false;
        public bool ExcludeAllDestroyedItems
        {
            get { return excludeAllDestroyedItems; }
            set { excludeAllDestroyedItems = value; }
        }

        private string unnamedLabelFormat = "Unnamed_{0:yyyy-MM-dd_HHmmss}";
        public string UnnamedLabelFormat
        {
            get { return unnamedLabelFormat; }
            set { unnamedLabelFormat = value; }
        }

        public ChangesetBuilder(WorkQueue workQueue, Logger logger, RevisionAnalyzer revisionAnalyzer)
            : base(workQueue, logger)
        {
            this.revisionAnalyzer = revisionAnalyzer;
        }

        public void BuildChangesets()
        {
            workQueue.AddLast(delegate (object work)
            {
                logger.WriteSectionSeparator();
                LogStatus(work, "Building changesets");

                var unnamedLabelCount = 0ul;

                var stopwatch = Stopwatch.StartNew();
                var pendingChangesByUser = new Dictionary<string, Changeset>();
                foreach (var dateEntry in revisionAnalyzer.SortedRevisions)
                {
                    var dateTime = dateEntry.Key;

                    //foreach (Revision revision in dateEntry.Value)
                    for (var node = dateEntry.Value.First; node != dateEntry.Value.Last.Next; node = node.Next)
                    {
                        var revision = node.Value;
                        
                        // VSS Destroyed Items: skip all. This will enable merging revisions separated by destroyed items.
                        if (excludeAllDestroyedItems)
                        {
                            if ((revision.Action as VssNamedAction)?.Name?.PhysicalName != null
                                                    && revisionAnalyzer.IsDestroyed((revision.Action as VssNamedAction).Name.PhysicalName)
                                                    && !revisionAnalyzer.Database.ItemExists((revision.Action as VssNamedAction).Name.PhysicalName))
                            {
                                continue;
                            }
                        }
                        
                        

                        // VSS Adds: fill in comments for add from create action
                        if (revision.Action.Type == VssActionType.Add && revision.Comment == null)
                            node.Value = revision = new Revision(revision.DateTime, revision.User, revision.Item, revision.Version, FindCorrespondingAction(revision, VssActionType.Create)?.Comment, revision.Action);

                        // VSS Labels: fill in empty label names as svn cannot have empty tag path.
                        if (revision.Action.Type == VssActionType.Label && string.IsNullOrEmpty(((VssLabelAction)revision.Action).Label))
                            node.Value = revision = new Revision(revision.DateTime, revision.User, revision.Item, revision.Version, revision.Comment, new VssLabelAction(string.Format(unnamedLabelFormat, revision.DateTime, ++unnamedLabelCount)));
                        
                        // VSS Creates: skip all and exclude from changeset as it increases history complexity unnecessarily and the info is not useful at all. not used in exporter
                        if (revision.Action.Type == VssActionType.Create)
                            continue;

                        // determine target of project revisions
                        var actionType = revision.Action.Type;
                        var namedAction = revision.Action as VssNamedAction;
                        var targetFile = revision.Item.PhysicalName;
                        if (namedAction != null)
                        {
                            targetFile = namedAction.Name.PhysicalName;
                        }

                        // Create actions are only used to obtain initial item comments;
                        // items are actually created when added to a project
                        var creating = (actionType == VssActionType.Create ||
                            (actionType == VssActionType.Branch && !revision.Item.IsProject));

                        // Share actions are never conflict (which is important,
                        // since Share always precedes Branch)
                        var nonconflicting = creating || (actionType == VssActionType.Share);


                        // look up the pending change for user of this revision
                        // and flush changes past time threshold
                        var pendingUser = revision.User;
                        Changeset pendingChange = null;
                        LinkedList<string> flushedUsers = new LinkedList<string>();
                        foreach (var userEntry in pendingChangesByUser)
                        {
                            var user = userEntry.Key;
                            var change = userEntry.Value;
                        
                            // flush change if file conflict or past time threshold
                            var flush = false;
                            var timeDiff = revision.DateTime - change.DateTime;

                            if (user == pendingUser)
                            {
                                // VSS Label: make labels on their own changeset
                                if ((revision.Action.Type == VssActionType.Label && change.Revisions.Last.Value.Action.Type != VssActionType.Label)
                                || (revision.Action.Type != VssActionType.Label && change.Revisions.Last.Value.Action.Type == VssActionType.Label))
                                {
                                    logger.WriteLine("NOTE: Splitting changeset due to label: {0}", change.Revisions.Last.Value.Action);
                                    flush = true;
                                }
                                // Cannot combine due to file conflict. must be recorded as separate changes
                                else if (!nonconflicting && change.TargetFiles.Contains(targetFile))
                                {
                                    logger.WriteLine("NOTE: Splitting changeset due to file conflict on {0}:", targetFile);
                                    flush = true;
                                }
                                

                            }
                            else
                            {

                            }
                            /*
                            if (!mergeAcrossDifferentUser && !HasSameUser(revision, change.Revisions.Last.Value))
                            {
                                logger.WriteLine("NOTE: Splitting changeset due to different user: {0} != {1}", change.Revisions.Last.Value.User, revision.User);
                                flush = true;
                            }
                            */

                            // additional check if not flushed above
                            if (!flush)
                            {
                                if ((TimeSpan.Zero == anyCommentThreshold ? TimeSpan.FromSeconds(1) : TimeSpan.Zero) + timeDiff > anyCommentThreshold)
                                {
                                    var lastRevision = FindLastRevisionWithNonEmptyComment(change);
                                    //if (HasSameComment(revision, change.Revisions.Last.Value))
                                    if (HasSameComment(revision, lastRevision))
                                    {
                                        string message;
                                        if ((TimeSpan.Zero == sameCommentThreshold ? TimeSpan.FromSeconds(1) : TimeSpan.Zero) + timeDiff < sameCommentThreshold)
                                        {
                                            message = "Using same-comment threshold";
                                        }
                                        else
                                        {
                                            message = "Splitting changeset due to same comment but exceeded threshold";
                                            logger.WriteLine("NOTE: {0} ({1} second gap):", message, timeDiff.TotalSeconds);
                                            flush = true;
                                        }
                                    }
                                    else
                                    {
                                        //logger.WriteLine("NOTE: Splitting changeset due to different comment: {0} != {1}:", change.Revisions.Last.Value.Comment, revision.Comment);
                                        logger.WriteLine("NOTE: Splitting changeset due to different comment: {0} != {1}:", lastRevision.Comment ?? "null", revision.Comment ?? "null");
                                        flush = true;
                                    }
                                }
                            }



                            if (flush)
                            {
                                AddChangeset(change);
                                flushedUsers.AddLast(user);
                            }
                            else if (user == pendingUser)
                            {
                                pendingChange = change;
                            }
                        }

                        foreach (string user in flushedUsers)
                            pendingChangesByUser.Remove(user);

                        // if no pending change for user, create a new one
                        if (pendingChange == null)
                        {
                            pendingChange = new Changeset();
                            pendingChange.User = pendingUser;
                            pendingChangesByUser[pendingUser] = pendingChange;
                        }

                        // update the time of the change based on the last revision
                        pendingChange.DateTime = revision.DateTime;

                        // add the revision to the change
                        pendingChange.Revisions.AddLast(revision);

                        // track target files in changeset to detect conflicting actions
                        if (!nonconflicting)
                        {
                            pendingChange.TargetFiles.Add(targetFile);
                        }

                        // build up a concatenation of unique revision comments
                        var revComment = revision.Comment;
                        if (revComment != null)
                        {
                            revComment = revComment.Trim();
                            if (revComment.Length > 0)
                            {
                                if (string.IsNullOrEmpty(pendingChange.Comment))
                                {
                                    pendingChange.Comment = revComment;
                                }
                                else if (!pendingChange.Comment.Contains(revComment))
                                {
                                    pendingChange.Comment += "\n" + revComment;
                                }
                            }
                        }
                    }
                }

                // flush all remaining changes
                foreach (var change in pendingChangesByUser.Values)
                {
                    AddChangeset(change);
                }
                stopwatch.Stop();

                logger.WriteSectionSeparator();
                logger.WriteLine("Found {0} changesets in {1:HH:mm:ss}",
                    changesets.Count, new DateTime(stopwatch.ElapsedTicks));
            });
        }

        private bool HasSameUser(Revision rev1, Revision rev2)
        {
            return (rev1.User ?? "").Trim() == (rev2.User ?? "").Trim();
        }

        private bool IsCommentMissing(Revision r)
        {
            return r.Comment == null;
        }

        private bool HasSameComment(Revision rev1, Revision rev2)
        {
            return (rev1.Comment ?? "").Trim() == (rev2.Comment ?? "").Trim();
        }

        private Revision FindLastRevisionWithNonEmptyComment(Changeset change)
        {
            var node = (LinkedListNode<Revision>)null;
            for (node = change.Revisions.Last; node != change.Revisions.First.Previous && string.IsNullOrEmpty(node.Value.Comment); node = node.Previous)
                ;
            return node?.Value??change.Revisions.Last.Value;
        }
        
        private Revision FindCorrespondingAction(Revision rev, VssActionType actionType)
        {
            if (rev.Action.Type != VssActionType.Edit && rev.Action.Type != VssActionType.Label)
            {
                var action = rev.Action as VssNamedAction;


                for (var node = revisionAnalyzer.SortedRevisions[rev.DateTime].Last; node != revisionAnalyzer.SortedRevisions[rev.DateTime].First.Previous; node = node.Previous)
                {
                    var revision = node.Value;
                    if (revision.Action.Type != VssActionType.Edit && revision.Action.Type != VssActionType.Label)
                    {
                        var targetaction = revision.Action as VssNamedAction;
                        if (action.Name.PhysicalName == targetaction.Name.PhysicalName && targetaction.Type == actionType && revision.Comment != null)
                            return revision;
                    }
                }

                // look into sortedrevisions for the comment
                var foundPrevKey = false;
                var foundNextKey = false;
                var prevKey = (DateTime)DateTime.MinValue;
                var nextKey = (DateTime)DateTime.MinValue;

                foreach (var key in revisionAnalyzer.SortedRevisions.Keys)
                {

                    if (key == rev.DateTime)
                    {
                        foundPrevKey = true;
                    }
                    else if (foundPrevKey)
                    {
                        foundNextKey = true;
                        nextKey = key;
                        break;
                    }

                    if (!foundPrevKey)
                        prevKey = key;
                }


                if (foundPrevKey && rev.DateTime - prevKey < TimeSpan.FromMinutes(15))
                    for (var node = revisionAnalyzer.SortedRevisions[prevKey].Last; node != revisionAnalyzer.SortedRevisions[prevKey].First.Previous; node = node.Previous)
                    {
                        var revision = node.Value;
                        if (revision.Action.Type != VssActionType.Edit && revision.Action.Type != VssActionType.Label)
                        {
                            var targetaction = revision.Action as VssNamedAction;
                            if (action.Name.PhysicalName == targetaction.Name.PhysicalName && targetaction.Type == actionType && revision.Comment != null)
                                return revision;
                        }
                    }

                if (foundNextKey && nextKey - rev.DateTime < TimeSpan.FromMinutes(15))
                    for (var node = revisionAnalyzer.SortedRevisions[nextKey].First; node != revisionAnalyzer.SortedRevisions[nextKey].Last.Next; node = node.Next)
                    {
                        var revision = node.Value;
                        if (revision.Action.Type != VssActionType.Edit && revision.Action.Type != VssActionType.Label)
                        {
                            var targetaction = revision.Action as VssNamedAction;
                            if (action.Name.PhysicalName == targetaction.Name.PhysicalName && targetaction.Type == actionType && revision.Comment != null)
                                return revision;
                        }
                    }
            }


            return null;
        }

        private void AddChangeset(Changeset change)
        {
            changesets.AddLast(change);
            int changesetId = changesets.Count;
            DumpChangeset(change, changesetId);
        }

        private void DumpChangeset(Changeset changeset, int changesetId)
        {
            var firstRevTime = changeset.Revisions.First.Value.DateTime;
            var changeDuration = changeset.DateTime - firstRevTime;
            logger.WriteSectionSeparator();
            logger.WriteLine("Changeset {0} - {1} ({2} secs) {3} {4} files",
                changesetId, changeset.DateTime, changeDuration.TotalSeconds, changeset.User,
                changeset.Revisions.Count);
            if (!string.IsNullOrEmpty(changeset.Comment))
            {
                logger.WriteLine(changeset.Comment);
            }
            logger.WriteLine();
            foreach (var revision in changeset.Revisions)
            {
                logger.WriteLine("  {0} {1}@{2} {3}",
                    revision.DateTime, revision.Item, revision.Version, revision.Action);
            }
        }
    }
}
