# StopGuessing
A system for protecting password-based authentication systems from online-guessing attacks.

#What is Password Guessing Attacks?
Password guessing attacks are separated into two categories:

1. Dictionary Attack: where the system uses a dictionary of common words to identify the password.

2. Brute Force Attack: where the system tries every possible combination of passcodes until it finds the correct one. 

#What Does StopGuessing Do?
StopGuessing protects password guessing attacks by keeping track of the number of failed attempts as well as the most recent login attempts. 

Failed attempts with invalid passwords will receive a penalty whereas successful attempts with the correct password will be rewarded.

#How To Use
1. Download the zip file or clone the repository on your machine
2. Navigate to the directory where StopGuessing is downloaded and click on the Visual Studio Solution to open the project.