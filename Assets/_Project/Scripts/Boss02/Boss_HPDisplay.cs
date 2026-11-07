using UnityEngine;
using UnityEngine.UI;

public class Boss_HPDisplay : MonoBehaviour
{
    private Image healthBar;
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        healthBar = gameObject.GetComponent<Image>();
    }

    // Update is called once per frame
    void Update()
    {

    }

    public void UpdateHealthDisplay(float currentHealth, float maxHealth)
    {
        if( healthBar == null)
        {
            healthBar = gameObject.GetComponent<Image>();
        }
        healthBar.fillAmount = currentHealth / maxHealth;
    }

    public void ImageColorChange(Color color)
    {
        healthBar.color = color;
    }
}