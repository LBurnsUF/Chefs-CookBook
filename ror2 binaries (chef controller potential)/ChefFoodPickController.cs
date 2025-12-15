// RoR2, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null
// ChefFoodPickController
using UnityEngine;

public class ChefFoodPickController : MonoBehaviour
{
	[SerializeField]
	public GameObject foodModel1;

	[SerializeField]
	public GameObject foodModel2;

	[SerializeField]
	public GameObject foodModel3;

	private void Start()
	{
		switch (Random.Range(0, 2))
		{
		case 0:
			foodModel1.SetActive(value: true);
			break;
		case 1:
			foodModel2.SetActive(value: true);
			break;
		case 2:
			foodModel3.SetActive(value: true);
			break;
		}
	}
}
