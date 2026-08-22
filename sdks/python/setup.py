from setuptools import setup, find_packages
from pathlib import Path

this_directory = Path(__file__).parent
long_description = (this_directory / "README.md").read_text()

setup(
    name="tokenbee-sdk",
    version="2.0.0",
    packages=find_packages(),
    install_requires=["httpx>=0.23.0"],
    author="TokenBee Inc.",
    author_email="founders@tokenbee.io",
    description="Official Python SDK for TokenBee LLM inference gateway and observability.",
    long_description=long_description,
    long_description_content_type="text/markdown",
    python_requires=">=3.7",
    classifiers=[
        "Programming Language :: Python :: 3",
        "License :: OSI Approved :: MIT License",
        "Operating System :: OS Independent",
    ],
    project_urls={
        "Homepage": "https://tokenbee.io",
        "Dashboard": "https://tokenbee.io/dashboard",
        "Source": "https://github.com/tokenBee/gateway",
    },
)