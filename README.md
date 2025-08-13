## Hold & Release Logic

The hold and release logic in this program works by monitoring when you press the **Shift** key while your mouse is over the target application’s window (like **HD-Player**).  

- **Press Shift once** → Sends a continuous **Shift Down** signal via the Interception driver, as if you’re holding the key indefinitely.  
- **Press Shift again** → Sends a **Shift Up** signal to stop holding it.  
- **Extra safety**: The program automatically sends **Shift Up** if you click the left mouse button or move the cursor outside the target window, ensuring the key is released when you leave the gameplay area.  
- This toggle effect is active **only** while the cursor is on the target application.

---

## Setup & Usage

1. **Install the Driver**  
   - Right-click **`setup.exe`** and select **Run as Administrator**.  
   - The driver will be installed automatically.  
   - Your PC will restart after installation.

2. **Run GonzalezShiftHolder**  
   - After restart, right-click **`GonzalezShiftHolder.exe`** and select **Run as Administrator**.  
   - Click **Enable Function** and then **Activate** to start.



