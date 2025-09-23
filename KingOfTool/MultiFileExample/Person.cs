using System;

namespace MultiFileExample
{
    public class Person
    {
        public string Name { get; set; }
        public int Age { get; set; }

        public Person(string name, int age)
        {
            Name = name;
            Age = age;
        }

        public void Introduce()
        {
            Console.WriteLine($"你好，我是{Name}，今年{Age}岁。");
        }

        public void Birthday()
        {
            Age++;
            Console.WriteLine($"{Name}过生日了！现在{Age}岁了。");
        }
    }
}