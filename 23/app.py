def calculate_average(numbers):
        if not numbers:
            return 0
        total = sum(numbers)
        return total / len(numbers)

def divide(a, b):
        if b == 0:
            raise ValueError("خطا: مقسوم‌علیه نمی‌تواند صفر باشد!")
        return a / b

def main():
    print("در حال اجرای برنامه محاسباتی...")
    data = [12, 18, 24]
    avg = calculate_average(data)
    print("میانگین:", avg)
    
    # فراخوانی تابع با پارامتر نامعتبر
    res = divide(100, 2)
    print("نتیجه تقسیم:", res)

if __name__ == "__main__":
    main()
