# Antymology

Antymology is a Unity-based simulation project that explores emergent behavior in ant colonies using Genetic Algorithms (GA). Agents (ants) evolve over generations, learning to forage, build nests, and sustain their queen through a voxel-based pheromone environment.

Table of contents
- Project Overview
- Features Implemented
- Technical Architecture
- Key Engineering Highlights
- How to Run & Test
- Controls & Configuration

## Project Overview

This simulation attempts to replicate the complex social behaviors of eusocial insects. Unlike traditional state machines where behavior is hard-coded, Antymology uses a neural-weight genome system. Ants sense their environment (pheromones, terrain type, health status) and make decisions based on evolved weights. Over thousands of simulation ticks, colonies that successfully build nests and keep their queen alive pass their genes to the next generation. The initial generations will behave relatively randomly; signs of evolution typically begin to emerge after the tenth generation. This manifests as the queen and worker ants locating each other via hormones, prompting the workers to transfer health to the queen, which in turn accelerates the production of nest blocks.

## Features Implemented

- Genetic Algorithm Engine: A custom evolutionary system handling population management, tournament selection, crossover, and mutation.

- Agent Specialization: Distinct roles for Queens (reproduction, nest building, pheromone broadcasting) and Workers (foraging, healing the queen, defense).

- Dynamic Pheromone System: A grid-based olfactory system where scents (Food, Danger, Queen, Worker) diffuse and decay in real-time.

- Voxel Interaction:

- Destructible Terrain: Workers consume MulchBlocks for health.

- Construction: Queens consume health to build NestBlocks.

- Hazards: AcidicBlocks cause accelerated health decay, encouraging avoidance behaviors.

- Simulation Speed Control: Decoupled logic ticks from frame rendering, allowing for high-speed training.

## Technical Architecture

The project follows a component-based architecture within Unity:

### The Manager Layer (SimulationManager.cs)

- Role: The brain of the simulation. It follows the Singleton pattern.

- Responsibilities:

- Manages the "Game Loop" (Fixed timestep logic independent of Unity Update).

- Handles the lifecycle of Episodes (Spawning -> Ticking -> Scoring -> Resetting).

- Executes the Genetic Algorithm (Selection, Mutation, Crossover).

### The Agent Layer (Ant.cs)

- Role: The individual unit of the simulation.

- Logic:

- Sensory Input: 9-dimensional input vector (Health status, Pheromone gradients, Surrounding blocks).

- Genome Processing: A float-array genome acts as weights for a decision matrix.

- Action Execution: Highest-weighted desires trigger actions (Move, Eat, Build, Transfer Health).

### The Environment Layer (WorldManager & PheromoneField)

- Role: Holds the state of the voxel world.

- Responsibilities: Efficient lookups for block types and pheromone intensity values at specific integer coordinates.

## How to Run & Test

### Prerequisites

- Unity Editor(Version 6000.3.xxx)

### Development Instructions

- Clone the Repository

- Open in Unity: Add the project folder to Unity Hub and open.

- Load the Scene: Navigate to Assets/Scenes/ and open MainSimulation.

- Run: Press the Play button in the Unity Editor.

### Verifying the GA

- Watch the Console window. You should see logs indicating:

- GA: Initialized population...

- Episode completion logs with Fitness scores.

- After EpisodesPerGenome * GenomePopulationSize runs, you will see GA Generation X completed logs.

## Controls & Configuration

All simulation parameters are exposed via the Inspector on the SimulationManager GameObject:

- TimeScale: Increase this (e.g. to 5.0) to fast-forward training.

- PopulationSize: Adjust the density of agents.





