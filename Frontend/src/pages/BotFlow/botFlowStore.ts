import { create } from 'zustand'
import { addEdge, applyNodeChanges, applyEdgeChanges, type Node, type Edge, type OnNodesChange, type OnEdgesChange, type OnConnect } from '@xyflow/react'

interface BotFlowStore {
  nodes: Node[]
  edges: Edge[]
  setNodes: (nodes: Node[]) => void
  setEdges: (edges: Edge[]) => void
  onNodesChange: OnNodesChange
  onEdgesChange: OnEdgesChange
  onConnect: OnConnect
  addNode: (node: Node) => void
  updateNodeData: (nodeId: string, data: Record<string, any>) => void
  deleteNode: (nodeId: string) => void
}

export const useBotFlowStore = create<BotFlowStore>((set, get) => ({
  nodes: [],
  edges: [],
  setNodes: (nodes) => set({ nodes }),
  setEdges: (edges) => set({ edges }),
  onNodesChange: (changes) => {
    set({
      nodes: applyNodeChanges(changes, get().nodes),
    })
  },
  onEdgesChange: (changes) => {
    set({
      edges: applyEdgeChanges(changes, get().edges),
    })
  },
  onConnect: (connection) => {
    // Add default connection styling if needed
    const edge = {
      ...connection,
      animated: true,
      style: { stroke: '#6366f1', strokeWidth: 2 },
    };
    set({
      edges: addEdge(edge, get().edges),
    })
  },
  addNode: (node) => {
    set({
      nodes: [...get().nodes, node],
    })
  },
  updateNodeData: (nodeId, data) => {
    set({
      nodes: get().nodes.map((node) => {
        if (node.id === nodeId) {
          return {
            ...node,
            data: {
              ...node.data,
              ...data,
            },
          }
        }
        return node
      }),
    })
  },
  deleteNode: (nodeId) => {
    set({
      nodes: get().nodes.filter((node) => node.id !== nodeId),
      edges: get().edges.filter((edge) => edge.source !== nodeId && edge.target !== nodeId),
    })
  },
}))
