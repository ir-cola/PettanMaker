public interface IHealth {
    int CurrentHP { get; }
    int MaxHP { get; }
    bool IsDead { get; }
    void TakeDamage(int amount);
}
