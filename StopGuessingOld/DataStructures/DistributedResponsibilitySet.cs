using System;
using System.Collections.Generic;
using System.Linq;
using StopGuessing.EncryptionPrimitives;

namespace StopGuessing.DataStructures
{
    public class DistributedResponsibilitySet {

        protected struct PhysicalNodesAssignedVirtualNodes
        {
            public int[] VirtualNodesAssigned;
            public int[,] VirtualNodes;

            public PhysicalNodesAssignedVirtualNodes(int numberOfPhysicalNodesPerVirtualNode, int maxVirtualNodesPerPhysicalNode)
            {
                VirtualNodesAssigned = new int[numberOfPhysicalNodesPerVirtualNode];
                VirtualNodes = new int[numberOfPhysicalNodesPerVirtualNode, maxVirtualNodesPerPhysicalNode];
                for (int i = 0; i < numberOfPhysicalNodesPerVirtualNode; i++)
                    for (int j = 0; j < maxVirtualNodesPerPhysicalNode; j++)
                        VirtualNodes[i, j] = -1;
            }
        }

        protected struct NodeRelationshipScore
        {
            public string PhysicalNode;
            public int VirtualNode;
            public ulong Score;
        }

        protected string ConfigurationKey;
        protected UniversalHashFunction KeyHash;
        public int NumberOfVirtualNodes { get; protected set; }
        public int NumberOfPhysicalNodesPerVirtualNode { get; protected set; }
        protected SortedSet<NodeRelationshipScore> SortedNodeRelationshipScores;
        protected Dictionary<string, NodeRelationshipScore[]> PhysicalNodeToNodeRelationships;
        protected HashSet<string> PhysicalNodes;
        protected string[,] VirtualNodesToPhysicalNodes;
        protected Object UpdateLock = new Object();



        public DistributedResponsibilitySet(
            string configurationKey,
            int numberOfVirtualNodes,
            int numberOfPhysicalNodesPerVirtualNode,
            IList<string> physicalNodes)
        {
            ConfigurationKey = configurationKey;
            KeyHash = new UniversalHashFunction(configurationKey);
            NumberOfVirtualNodes = numberOfVirtualNodes;
            NumberOfPhysicalNodesPerVirtualNode = numberOfPhysicalNodesPerVirtualNode;
            SortedNodeRelationshipScores = new SortedSet<NodeRelationshipScore>(new NodeRelationshipScoreComparer());
            PhysicalNodeToNodeRelationships = new Dictionary<string, NodeRelationshipScore[]>();
            PhysicalNodes = new HashSet<string>();
            Add(physicalNodes);
        }

        public uint FindVirtualNodeResponsible(string key)
        {
            ulong virtualNode = KeyHash.Hash(key) % (ulong) NumberOfVirtualNodes;
            return (uint) virtualNode;
        }

        public string[] FindPhysicalNodesForVirtualNode(uint virtualNode)
        {
            string[] physicalNodes = new string[NumberOfPhysicalNodesPerVirtualNode];
            for (int i = 0; i < NumberOfPhysicalNodesPerVirtualNode; i++)
                physicalNodes[i] = VirtualNodesToPhysicalNodes[i, virtualNode];
            return physicalNodes;
        }

        public string[] FindPhysicalNodesResponsible(string key)
        {
            uint virtualNode = FindVirtualNodeResponsible(key);
            return FindPhysicalNodesForVirtualNode(virtualNode);
        }

        protected class NodeRelationshipScoreComparer : IComparer<NodeRelationshipScore>
        {
            public int Compare(NodeRelationshipScore x, NodeRelationshipScore y)
            {
                if (x.Score < y.Score)
                    return -1;
                else if (x.Score > y.Score)
                    return 1;
                else
                    return String.CompareOrdinal(x.PhysicalNode, y.PhysicalNode);
            }
        }

        protected void AddInsideLock(string physicalNode)
        {
            if (!PhysicalNodeToNodeRelationships.ContainsKey(physicalNode))
            {
                NodeRelationshipScore[] nodeRelationshipScores = new NodeRelationshipScore[NumberOfVirtualNodes];
                UniversalHashFunction hash = new UniversalHashFunction(ConfigurationKey + physicalNode);
                for (int i = 0; i < NumberOfVirtualNodes; i++)
                {
                    nodeRelationshipScores[i].PhysicalNode = physicalNode;
                    nodeRelationshipScores[i].VirtualNode = i;
                    nodeRelationshipScores[i].Score = hash.Hash(i);
                }
            }
            foreach (NodeRelationshipScore nrs in PhysicalNodeToNodeRelationships[physicalNode])
                SortedNodeRelationshipScores.Add(nrs);
            PhysicalNodes.Add(physicalNode);
        }

        public void Add(string physicalNode)
        {
            lock (UpdateLock)
            {
                AddInsideLock(physicalNode);
                UpdateInsideLock();
            }
        }

        public void Add(IList<string> physicalNodes)
        {
            lock (UpdateLock)
            {
                foreach (string physicalNode in physicalNodes)
                    AddInsideLock(physicalNode);
                UpdateInsideLock();
            }
        }

        public void RemoveInsideLock(string physicalNode)
        {
            if (PhysicalNodes.Contains(physicalNode) && PhysicalNodeToNodeRelationships.ContainsKey(physicalNode))
            {
                foreach (NodeRelationshipScore nrs in PhysicalNodeToNodeRelationships[physicalNode])
                    SortedNodeRelationshipScores.Remove(nrs);
            }
        }

        public void Remove(string physicalNode)
        {
            lock (UpdateLock)
            {
                RemoveInsideLock(physicalNode);
                UpdateInsideLock();
            }
        }

        public void Remove(IList<string> physicalNodes)
        {
            lock (UpdateLock)
            {
                foreach (string physicalNode in physicalNodes)
                    RemoveInsideLock(physicalNode);
                UpdateInsideLock();
            }
        }

        public void SetPhysicalNodes(IList<string> newSetOfPhysicalNodes)
        {
            lock (UpdateLock)
            {
                string[] physicalNodesAdded = newSetOfPhysicalNodes.Where(node => !PhysicalNodes.Contains(node)).ToArray();
                string[] physicalNodesRemoved = PhysicalNodes.Where(node => !newSetOfPhysicalNodes.Contains(node)).ToArray();
                foreach (string physicalNode in physicalNodesAdded)
                    AddInsideLock(physicalNode);
                foreach (string physicalNode in physicalNodesRemoved)
                    RemoveInsideLock(physicalNode);
                PhysicalNodes = new HashSet<string>(newSetOfPhysicalNodes);
                UpdateInsideLock();
            }
        }

        protected void UpdateInsideLock()
        {
            Dictionary<string, PhysicalNodesAssignedVirtualNodes> newPhysicalNodesToVirtualNodes = new Dictionary<string, PhysicalNodesAssignedVirtualNodes>();
            string[,] newVirtualNodesToPhysicalNodes = new string[NumberOfPhysicalNodesPerVirtualNode, NumberOfVirtualNodes];
            int maxNumberOfVirtualNodesPerPhysicalNode = (NumberOfVirtualNodes/ PhysicalNodes.Count) + 2; // FIXME?
            foreach (string physicalNode in PhysicalNodes)
            {
                newPhysicalNodesToVirtualNodes[physicalNode] = 
                    new PhysicalNodesAssignedVirtualNodes(NumberOfPhysicalNodesPerVirtualNode, maxNumberOfVirtualNodesPerPhysicalNode);
            }

            foreach (NodeRelationshipScore nodePair in SortedNodeRelationshipScores)
            {
                for (int i = 0; i < NumberOfPhysicalNodesPerVirtualNode; i++)
                {
                    if (newVirtualNodesToPhysicalNodes[i, nodePair.VirtualNode] == null &&
                        newPhysicalNodesToVirtualNodes[nodePair.PhysicalNode].VirtualNodesAssigned[i] <
                        maxNumberOfVirtualNodesPerPhysicalNode)

                    {
                        int virtualNodeIndex =
                            newPhysicalNodesToVirtualNodes[nodePair.PhysicalNode].VirtualNodesAssigned[i]++;
                        newPhysicalNodesToVirtualNodes[nodePair.PhysicalNode].VirtualNodes[i, virtualNodeIndex] =
                            nodePair.VirtualNode;
                        newVirtualNodesToPhysicalNodes[i, nodePair.VirtualNode] = nodePair.PhysicalNode;
                        // This physical node should be assigned to a virtual node AT MOST once
                        break;
                    }
                }
            }
            VirtualNodesToPhysicalNodes = newVirtualNodesToPhysicalNodes;
        }



    }
}
