namespace UltraSim.Client;

public delegate TState InterpolationLerp<TState>(in TState from, in TState to, float alpha);
